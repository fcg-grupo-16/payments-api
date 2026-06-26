using Fcg.Payments.Consumers;
using Fcg.Payments.Payments;
using MassTransit;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<PaymentsOptions>(
    builder.Configuration.GetSection(PaymentsOptions.SectionName));

builder.Services.AddMassTransit(x =>
{
    // Prefixo por serviço garante filas distintas entre microsserviços.
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

var app = builder.Build();

app.MapHealthChecks("/health");

app.Run();
