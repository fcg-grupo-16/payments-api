using Fcg.Contracts.Events;
using Fcg.Payments.Payments;
using MassTransit;
using Microsoft.Extensions.Options;

namespace Fcg.Payments.Consumers;

/// <summary>
/// Consome <see cref="OrderPlacedEvent"/>, simula o processamento do pagamento,
/// calcula a decisão e publica <see cref="PaymentProcessedEvent"/>.
/// </summary>
public sealed class OrderPlacedConsumer : IConsumer<OrderPlacedEvent>
{
    private static readonly TimeSpan SimulatedProcessingDelay = TimeSpan.FromMilliseconds(500);

    private readonly ILogger<OrderPlacedConsumer> _logger;
    private readonly PaymentsOptions _options;

    public OrderPlacedConsumer(ILogger<OrderPlacedConsumer> logger, IOptions<PaymentsOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public async Task Consume(ConsumeContext<OrderPlacedEvent> context)
    {
        var order = context.Message;

        _logger.LogInformation(
            "Pedido recebido para processamento de pagamento. OrderId={OrderId}, UserId={UserId}, GameId={GameId}, Valor={Price}",
            order.OrderId, order.UserId, order.GameId, order.Price);

        // Simula um tempo fixo de processamento do pagamento.
        await Task.Delay(SimulatedProcessingDelay, context.CancellationToken);

        var status = PaymentDecision.Decide(order.Price, _options.MaxApprovedAmount);

        _logger.LogInformation(
            "Pagamento processado. OrderId={OrderId}, Status={Status} (limite de aprovação: {Limit})",
            order.OrderId, status, _options.MaxApprovedAmount);

        await context.Publish(new PaymentProcessedEvent
        {
            OrderId = order.OrderId,
            UserId = order.UserId,
            GameId = order.GameId,
            Price = order.Price,
            Status = status,
        });
    }
}
