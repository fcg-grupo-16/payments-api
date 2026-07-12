using Fcg.Payments.Consumers;
using Fcg.Payments.Payments;
using MassTransit;
using Fcg.Payments.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<PaymentsOptions>(
    builder.Configuration.GetSection(PaymentsOptions.SectionName));

// Ativa o suporte básico aos Controllers (Rotas)
builder.Services.AddControllers();

builder.Services.AddMassTransit(x =>
{
    x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter("payments", false));
    x.AddConsumer<OrderPlacedConsumer>();
    x.UsingRabbitMq((ctx, cfg) =>
    {
        var host = builder.Configuration["RabbitMq:Host"] ?? "localhost";
        var user = builder.Configuration["RabbitMq:Username"] ?? "guest";
        var pass = builder.Configuration["RabbitMq:Password"] ?? "guest";
        cfg.Host(host, "/", h => { h.Username(user); h.Password(pass); });
        cfg.ConfigureEndpoints(ctx);
    });
});

builder.Services.AddHealthChecks();
builder.Services.AddScoped<IPaymentService, PixPaymentService>();

var app = builder.Build();

app.MapHealthChecks("/health");

// Mapeia os endpoints dos controllers ativos
app.MapControllers();

app.Run();
