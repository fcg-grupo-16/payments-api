namespace Fcg.Payments.Payments;

/// <summary>
/// Resultado de uma decisão de pagamento: o status e a regra que o determinou.
/// </summary>
/// <remarks>
/// Antes o <see cref="IPaymentDecider"/> devolvia apenas a <c>string</c> do status, e a identidade
/// da regra se perdia dentro do <c>Any(...)</c>. Sem ela não há como responder "por que este
/// pagamento foi rejeitado?" senão lendo log e cruzando na mão — que é exatamente a lacuna que a
/// issue #19 aponta.
/// </remarks>
/// <param name="Status">"Approved" ou "Rejected" (ver <see cref="PaymentDecision"/>).</param>
/// <param name="Rule">Nome da regra que rejeitou, ou <see cref="SemRegra"/> quando aprovado.</param>
public sealed record PaymentOutcome(string Status, string Rule)
{
    /// <summary>Valor do label <c>rule</c> quando nenhuma regra rejeitou.</summary>
    /// <remarks>
    /// Um valor explícito, e não string vazia ou <c>null</c>: o Prometheus trata label ausente e
    /// label vazio de formas diferentes nas queries, e "None" deixa a série legível no dashboard.
    /// </remarks>
    public const string SemRegra = "None";

    public static PaymentOutcome Aprovado() => new(PaymentDecision.Approved, SemRegra);

    public static PaymentOutcome Rejeitado(string rule) => new(PaymentDecision.Rejected, rule);
}
