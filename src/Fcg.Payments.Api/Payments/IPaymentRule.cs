namespace Fcg.Payments.Payments;

/// <summary>
/// Uma regra de decisão de pagamento. Cada regra avalia o pedido de forma independente;
/// se QUALQUER regra rejeitar, o pagamento é <see cref="PaymentDecision.Rejected"/>.
/// </summary>
public interface IPaymentRule
{
    /// <summary>
    /// Identificador estável da regra, usado como label da métrica
    /// <c>fcg_payment_decisions_total</c> e como atributo de span.
    /// </summary>
    /// <remarks>
    /// É uma constante EXPLÍCITA, e não <c>GetType().Name</c>, de propósito: com o nome derivado do
    /// tipo, renomear a classe mudaria silenciosamente um label de métrica — quebrando dashboards e
    /// alertas que dependem dele, sem nenhum erro de compilação. Aqui o nome é contrato.
    /// </remarks>
    string Name { get; }

    /// <returns><c>true</c> se esta regra REJEITA o pagamento.</returns>
    bool Rejects(PaymentContext context);
}
