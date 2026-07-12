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

        // Persiste o registro de auditoria ANTES de publicar. Idempotente por OrderId (índice único):
        // uma reentrega não gera documento duplicado.
        var inserido = await _repository.RegistrarAsync(new Payment
        {
            OrderId = order.OrderId.ToString(),
            UserId = order.UserId,
            GameId = order.GameId,
            Price = order.Price,
            Status = status,
            ProcessedAt = DateTime.UtcNow,
        }, ct);

        _logger.LogInformation(
            "Pagamento processado. OrderId={OrderId}, Status={Status} (limite de aprovação: {Limit}, novo registro: {Inserido})",
            order.OrderId, status, _options.MaxApprovedAmount, inserido);

        // Publicamos SEMPRE (inclusive em reentrega, quando inserido==false): a decisão é
        // determinística (mesmo Price -> mesmo Status), então não há divergência, e a semântica
        // é at-least-once — o consumer do catalog é idempotente (só transiciona de Pending).
        // A deduplicação de CONSUMO (não reprocessar de fato) é escopo da idempotência (#1, inbox).
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
