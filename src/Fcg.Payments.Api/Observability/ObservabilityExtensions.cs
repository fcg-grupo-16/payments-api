using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Fcg.Payments.Observability;

/// <summary>
/// Instrumentação de observabilidade do serviço (Fase 3): métricas no formato Prometheus
/// (expostas em <c>/metrics</c>) e traces distribuídos exportados por OTLP.
/// </summary>
public static class ObservabilityExtensions
{
    /// <summary>
    /// Registra OpenTelemetry para métricas e traces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>As métricas HTTP aqui são quase vazias, e isso está CORRETO.</b> Este serviço não tem
    /// controller: é um worker que consome do RabbitMQ. O <c>/metrics</c> vai expor basicamente as
    /// probes de health e as métricas de runtime. O valor deste serviço está nos TRACES e na
    /// MÉTRICA DE NEGÓCIO (<see cref="PaymentMetrics"/>) — não "conserte" isso achando que o
    /// histograma HTTP vazio é defeito.
    /// </para>
    /// <para>
    /// <b>Traces — o ponto central.</b> O MassTransit 8 emite spans de consume/publish e propaga
    /// contexto W3C nos headers da mensagem. O <c>.AddSource("MassTransit")</c> é o que costura
    /// <c>catalog-api → RabbitMQ → payments-api → RabbitMQ → catalog-api</c> num ÚNICO trace. Sem
    /// ele, este serviço não continua o contexto recebido nem o propaga adiante, e a cadeia da
    /// compra se parte em traces órfãos — que era o estado medido antes desta issue.
    /// </para>
    /// <para>
    /// <b>Configuração.</b> Tudo por variável de ambiente padrão do OTel
    /// (<c>OTEL_EXPORTER_OTLP_ENDPOINT</c>, <c>OTEL_SERVICE_NAME</c>), já provisionadas pelo
    /// ConfigMap do repo <c>orchestration</c> — nenhuma mudança de manifesto é necessária.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // OTEL_SERVICE_NAME é lido automaticamente pelo SDK; o fallback cobre execução local sem
        // env var (dotnet run), para o serviço não aparecer como "unknown_service".
        var serviceName = configuration["OTEL_SERVICE_NAME"] ?? "payments-api";

        // Endpoint OTLP AUSENTE => traces desligados de propósito. Mantém `dotnet run` e os testes
        // funcionando sem Jaeger no ar (senão o exportador tenta conectar e enche o log de erro a
        // cada intervalo de export).
        var otlpEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

        var otel = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: serviceName,
                    serviceVersion: typeof(ObservabilityExtensions).Assembly.GetName().Version?.ToString() ?? "0.0.0")
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = environment.EnvironmentName
                }));

        otel.WithMetrics(metrics => metrics
            // SEM esta linha o counter incrementa e a métrica nunca é exportada. Ver PaymentMetrics.
            .AddMeter(PaymentMetrics.MeterName)
            .AddAspNetCoreInstrumentation()
            .AddRuntimeInstrumentation()
            .AddPrometheusExporter());

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            otel.WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                {
                    // NÃO gere trace para /health* e /metrics: são chamados a cada 10s pelas probes
                    // e a cada 15s pelo Prometheus, e afogariam o Jaeger com ruído.
                    options.Filter = context =>
                    {
                        var path = context.Request.Path.Value ?? string.Empty;
                        return !path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
                            && !path.StartsWith("/metrics", StringComparison.OrdinalIgnoreCase);
                    };
                    options.RecordException = true;
                })
                .AddSource("MassTransit")
                .AddOtlpExporter());
        }

        return services;
    }
}
