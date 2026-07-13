using System.Globalization;
using Fcg.Payments.Consumers;
using Fcg.Payments.Payments;
using Fcg.Payments.Payments.Rules;
using Fcg.Payments.Persistence;
using MassTransit;
using MongoDB.Driver;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<PaymentsOptions>(
    builder.Configuration.GetSection(PaymentsOptions.SectionName));

// MongoDB (paymentsdb) — auditoria dos pagamentos. Config por ambiente (12-factor), na convenção
// da plataforma (MongoDbSettings__*, provisionada no orchestration), com fallback local.
var mongoConnectionString = builder.Configuration["MongoDbSettings:ConnectionString"]
    ?? "mongodb://localhost:27017/?replicaSet=rs0";
var mongoDatabaseName = builder.Configuration["MongoDbSettings:DatabaseName"] ?? "paymentsdb";

builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(mongoConnectionString));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(mongoDatabaseName));
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();

// Regras de decisão plugáveis (qualquer rejeição -> Rejected) + fonte de aleatoriedade injetável.
builder.Services.AddSingleton<IRandomSource, RandomSource>();
builder.Services.AddSingleton<IPaymentRule, AmountLimitRule>();
builder.Services.AddSingleton<IPaymentRule, BlockedUserRule>();
builder.Services.AddSingleton<IPaymentRule, BlockedGameRule>();
builder.Services.AddSingleton<IPaymentRule, RandomFailureRule>();
builder.Services.AddSingleton<IPaymentDecider, PaymentDecider>();

// Config do RabbitMQ (reutilizada pela mensageria e pelo health check).
var rabbitHost = builder.Configuration["RabbitMq:Host"] ?? "localhost";
var rabbitUser = builder.Configuration["RabbitMq:Username"] ?? "guest";
var rabbitPass = builder.Configuration["RabbitMq:Password"] ?? "guest";

builder.Services.AddMassTransit(x =>
{
    // Prefixo por serviço garante filas distintas entre microsserviços.
    x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter("payments", false));
    x.AddConsumer<OrderPlacedConsumer>();
    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(rabbitHost, "/", h => { h.Username(rabbitUser); h.Password(rabbitPass); });

        // Scheduler de mensagens atrasadas — usa o plugin rabbitmq_delayed_message_exchange do
        // broker (imagem custom do orchestration, v0.7.0). Necessário para o delayed redelivery.
        cfg.UseDelayedMessageScheduler();

        // Redelivery atrasado (second-level retry): esgotado o retry imediato, a mensagem é
        // devolvida ao broker com intervalos CRESCENTES (default 60/300/900s) antes de ir para a
        // _error. Configurável via RabbitMq:DelayedRedeliverySeconds (curto em testes).
        cfg.UseDelayedRedelivery(r => r.Intervals(ParseDelayedIntervals(
            builder.Configuration["RabbitMq:DelayedRedeliverySeconds"])));

        // Retry imediato (first-level), EXPONENCIAL com limite. Esgotados retry + redelivery, a
        // mensagem vai para payments-order-placed-event_error (dead-letter), sem ser perdida.
        // Só aceita um inteiro >= 0; negativo/ inválido cai no default (3) — não derruba o startup.
        var immediateRetries = int.TryParse(builder.Configuration["RabbitMq:ImmediateRetryCount"], out var ir) && ir >= 0 ? ir : 3;
        cfg.UseMessageRetry(r => r.Exponential(immediateRetries,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(3)));

        cfg.ConfigureEndpoints(ctx);
    });
});

// Conexão RabbitMQ ÚNICA e reutilizada pelo health check. Antes o AddRabbitMQ abria uma conexão
// nova a cada readiness sem fechá-la (leak que saturava o broker). A factory cria a conexão UMA vez
// e a reusa em todas as checagens — com auto-recovery para reconectar quando o broker volta. O lock
// (double-checked) evita a criação concorrente se dois probes chegarem simultaneamente; se a conexão
// estiver fechada (recovery esgotado) ela é descartada e RECRIADA na próxima check — por isso não
// usamos Lazy<Task<IConnection>>, que cachearia uma Task falhada (broker fora no 1º check) e deixaria
// o readiness preso em 503 mesmo após o broker voltar. Lazy e assíncrona (sem sync-over-async, sem
// bloquear o startup): o processo sobe mesmo com o broker fora/lento, o check reporta 503, e uma
// tentativa futura reconecta (200).
var healthRabbitLock = new SemaphoreSlim(1, 1);
IConnection? healthRabbitConnection = null;

// Health checks das DEPENDÊNCIAS, tagueadas "ready" (entram no /health/ready):
// - Mongo: AddMongoDb reusa o IMongoClient singleton (sem abrir conexão por check).
// - RabbitMQ: factory lazy que reusa uma única IConnection (sem leak); um canal por check detecta
//   o broker fora (503) e a auto-recovery/recriação reconecta quando ele volta (200).
builder.Services.AddHealthChecks()
    .AddMongoDb(sp => sp.GetRequiredService<IMongoClient>(), name: "mongodb", tags: ["ready"])
    .AddRabbitMQ(
        factory: async sp =>
        {
            if (healthRabbitConnection?.IsOpen == true)
                return healthRabbitConnection;

            await healthRabbitLock.WaitAsync();
            try
            {
                if (healthRabbitConnection?.IsOpen == true)
                    return healthRabbitConnection;

                // A conexão anterior está fechada (recovery esgotado) — descarta antes de recriar.
                if (healthRabbitConnection is not null)
                {
                    await healthRabbitConnection.DisposeAsync();
                    healthRabbitConnection = null;
                }

                healthRabbitConnection = await new ConnectionFactory
                {
                    HostName = rabbitHost,
                    UserName = rabbitUser,
                    Password = rabbitPass,
                    AutomaticRecoveryEnabled = true
                }.CreateConnectionAsync();
                return healthRabbitConnection;
            }
            finally
            {
                healthRabbitLock.Release();
            }
        },
        name: "rabbitmq",
        tags: ["ready"]);

var app = builder.Build();

// Liveness: só o processo (sem dependências). Readiness: dependências tagueadas "ready".
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
// Agregado legado (compat): todos os checks.
app.MapHealthChecks("/health");

// Garante o índice único em OrderId no startup (idempotente).
using (var scope = app.Services.CreateScope())
{
    var repo = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
    await repo.GarantirIndicesAsync();
}

await app.RunAsync();

// Libera recursos da conexão de health check após o host encerrar (sem concorrência possível).
if (healthRabbitConnection is not null)
    await healthRabbitConnection.DisposeAsync();
healthRabbitLock.Dispose();

// Parse tolerante de RabbitMq:DelayedRedeliverySeconds. Ignora entradas inválidas/não-positivas e
// faz fallback para os defaults (60/300/900s) se ausente/vazia/toda inválida — config ruim não
// pode derrubar o serviço no startup.
static TimeSpan[] ParseDelayedIntervals(string? raw)
{
    var defaults = new[] { TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(900) };
    if (string.IsNullOrWhiteSpace(raw))
    {
        return defaults;
    }

    var parsed = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(s => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
                && v > 0 && double.IsFinite(v) && v <= TimeSpan.MaxValue.TotalSeconds
            ? TimeSpan.FromSeconds(v)
            : (TimeSpan?)null)
        .Where(t => t.HasValue)
        .Select(t => t!.Value)
        .ToArray();

    return parsed.Length > 0 ? parsed : defaults;
}
