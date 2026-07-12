namespace Fcg.Payments.Payments;

/// <summary>
/// Uma regra de decisão de pagamento. Cada regra avalia o pedido de forma independente;
/// se QUALQUER regra rejeitar, o pagamento é <see cref="PaymentDecision.Rejected"/>.
/// </summary>
public interface IPaymentRule
{
    /// <returns><c>true</c> se esta regra REJEITA o pagamento.</returns>
    bool Rejects(PaymentContext context);
}
