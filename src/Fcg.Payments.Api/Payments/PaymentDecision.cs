namespace Fcg.Payments.Payments;

/// <summary>
/// Regra de decisão de pagamento (determinística para tornar o demo reproduzível).
/// Aprova quando o valor é menor ou igual ao limite configurado; caso contrário, rejeita.
/// </summary>
public static class PaymentDecision
{
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";

    /// <summary>
    /// Decide o status do pagamento com base no valor e no limite de aprovação.
    /// </summary>
    /// <param name="price">Valor do pedido.</param>
    /// <param name="maxApprovedAmount">Limite máximo aprovado automaticamente.</param>
    /// <returns><see cref="Approved"/> se <paramref name="price"/> &lt;= <paramref name="maxApprovedAmount"/>; senão <see cref="Rejected"/>.</returns>
    public static string Decide(decimal price, decimal maxApprovedAmount)
        => price <= maxApprovedAmount ? Approved : Rejected;
}
