using System.Diagnostics;
using Fcg.Contracts.Events;
using Fcg.Payments.Observability;
using Fcg.Payments.Payments;
using Fcg.Payments.Persistence;
using MassTransit;

namespace Fcg.Payments.Consumers;

/// <summary>
/// Consome <see cref="OrderPlacedEvent"/>, simula o processamento do pagamento,
/// calcula a decisão, PERSISTE o registro (auditoria) e publica <see cref="PaymentProcessedEvent"/>.
/// </summary>
public sealed class OrderPlacedConsumer : IConsumer<OrderPlacedEvent>
{
    private static readonly TimeSpan SimulatedProcessingDelay = TimeSpan.FromMilliseconds(500);

    private readonly ILogger<OrderPlacedConsumer> _logger;
    private readonly IPaymentDecider _decider;
    private readonly IPaymentRepository _repository;

    public OrderPlacedConsumer(
        ILogger<OrderPlacedConsumer> logger,
        IPaymentDecider decider,
        IPaymentRepository repository)
    {
        _logger = logger;
        _decider = decider;
        _repository = repository;
    }

    public async Task Consume(ConsumeContext<OrderPlacedEvent> context)
    {
        var order = context.Message;
        var ct = context.CancellationToken;
        var cronometro = Stopwatch.StartNew();

        // Enriquece o span que o MassTransit JÁ criou, em vez de abrir um novo: assim os atributos
        // ficam no span certo do trace distribuído, sem um nível extra de aninhamento.
        // ⚠️ Ids entram como ATRIBUTO DE SPAN, nunca como label de métrica — ver PaymentMetrics.
        var activity = Activity.Current;
        activity?.SetTag("fcg.order.id", order.OrderId.ToString());
        activity?.SetTag("fcg.user.id", order.UserId);
        activity?.SetTag("fcg.game.id", order.GameId);
        activity?.SetTag("fcg.order.price", order.Price);

        _logger.LogInformation(
            "Pedido recebido para processamento de pagamento. OrderId={OrderId}, UserId={UserId}, GameId={GameId}, Valor={Price}",
            order.OrderId, order.UserId, order.GameId, order.Price);

        // Simula um tempo fixo de processamento do pagamento.
        await Task.Delay(SimulatedProcessingDelay, ct);

        var outcome = _decider.Decide(new PaymentContext(order.UserId, order.GameId, order.Price));

        // Idempotência por OrderId: a coleção `payments` (índice único em OrderId) é o registro
        // dos pedidos já processados. RegistrarAsync insere e retorna false se o OrderId já existe.
        var inserido = await _repository.RegistrarAsync(new Payment
        {
            OrderId = order.OrderId.ToString(),
            UserId = order.UserId,
            GameId = order.GameId,
            Price = order.Price,
            Status = outcome.Status,
            ProcessedAt = DateTime.UtcNow,
        }, ct);

        if (!inserido)
        {
            // Pedido já processado (reentrega/duplicata): descarta sem erro e SEM republicar,
            // garantindo um único PaymentProcessedEvent por OrderId.
            //
            // A duplicata NÃO conta como decisão nova: contaria o mesmo pagamento duas vezes e
            // distorceria a taxa de aprovação do dashboard. Vira uma série própria, que as queries
            // de taxa EXCLUEM (ver README, seção de observabilidade).
            PaymentMetrics.RecordDecision(PaymentMetrics.StatusDuplicate, PaymentMetrics.RuleIdempotency);
            activity?.SetTag("fcg.payment.duplicate", true);

            _logger.LogInformation(
                "Pagamento duplicado descartado — OrderId já processado. OrderId={OrderId}",
                order.OrderId);
            return;
        }

        // A decisão só é contabilizada DEPOIS do insert vencer: a decisão é calculada antes dele,
        // mas registrá-la aqui garante uma contagem por pagamento, não por entrega de mensagem.
        activity?.SetTag("fcg.payment.status", outcome.Status);
        activity?.SetTag("fcg.payment.rule", outcome.Rule);
        PaymentMetrics.RecordDecision(outcome.Status, outcome.Rule);
        PaymentMetrics.RecordDuration(cronometro.Elapsed.TotalSeconds);

        _logger.LogInformation(
            "Pagamento processado. OrderId={OrderId}, Status={Status}, Regra={Rule}",
            order.OrderId, outcome.Status, outcome.Rule);

        await context.Publish(new PaymentProcessedEvent
        {
            OrderId = order.OrderId,
            UserId = order.UserId,
            GameId = order.GameId,
            Price = order.Price,
            Status = outcome.Status,
        });
    }
}
