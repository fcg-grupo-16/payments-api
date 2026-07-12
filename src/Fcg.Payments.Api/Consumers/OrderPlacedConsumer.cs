using Fcg.Contracts.Events;
using Fcg.Payments.Payments;
using Fcg.Payments.Persistence;
using MassTransit;
using Microsoft.Extensions.Options;

namespace Fcg.Payments.Consumers;

/// <summary>
/// Consome <see cref="OrderPlacedEvent"/>, simula o processamento do pagamento,
/// calcula a decisão, PERSISTE o registro (auditoria) e publica <see cref="PaymentProcessedEvent"/>.
/// </summary>
public sealed class OrderPlacedConsumer : IConsumer<OrderPlacedEvent>
{
    private static readonly TimeSpan SimulatedProcessingDelay = TimeSpan.FromMilliseconds(500);

    private readonly ILogger<OrderPlacedConsumer> _logger;
    private readonly PaymentsOptions _options;
    private readonly IPaymentRepository _repository;

    public OrderPlacedConsumer(
        ILogger<OrderPlacedConsumer> logger,
        IOptions<PaymentsOptions> options,
        IPaymentRepository repository)
    {
        _logger = logger;
        _options = options.Value;
        _repository = repository;
    }

    public async Task Consume(ConsumeContext<OrderPlacedEvent> context)
    {
        var order = context.Message;
        var ct = context.CancellationToken;

        _logger.LogInformation(
            "Pedido recebido para processamento de pagamento. OrderId={OrderId}, UserId={UserId}, GameId={GameId}, Valor={Price}",
            order.OrderId, order.UserId, order.GameId, order.Price);

        // Simula um tempo fixo de processamento do pagamento.
        await Task.Delay(SimulatedProcessingDelay, ct);

        var status = PaymentDecision.Decide(order.Price, _options.MaxApprovedAmount);

        // Idempotência por OrderId: a coleção `payments` (índice único em OrderId) é o registro
        // dos pedidos já processados. RegistrarAsync insere e retorna false se o OrderId já existe.
        var inserido = await _repository.RegistrarAsync(new Payment
        {
            OrderId = order.OrderId.ToString(),
            UserId = order.UserId,
            GameId = order.GameId,
            Price = order.Price,
            Status = status,
            ProcessedAt = DateTime.UtcNow,
        }, ct);

        if (!inserido)
        {
            // Pedido já processado (reentrega/duplicata): descarta sem erro e SEM republicar,
            // garantindo um único PaymentProcessedEvent por OrderId.
            _logger.LogInformation(
                "Pagamento duplicado descartado — OrderId já processado. OrderId={OrderId}",
                order.OrderId);
            return;
        }

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
