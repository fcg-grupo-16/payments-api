namespace Fcg.Payments.Payments;

/// <summary>Combina as <see cref="IPaymentRule"/> registradas para decidir o status do pagamento.</summary>
public interface IPaymentDecider
{
    /// <returns><see cref="PaymentDecision.Rejected"/> se qualquer regra rejeitar; senão <see cref="PaymentDecision.Approved"/>.</returns>
    string Decide(PaymentContext context);
}

public sealed class PaymentDecider(IEnumerable<IPaymentRule> rules) : IPaymentDecider
{
    public string Decide(PaymentContext context) =>
        rules.Any(rule => rule.Rejects(context)) ? PaymentDecision.Rejected : PaymentDecision.Approved;
}
