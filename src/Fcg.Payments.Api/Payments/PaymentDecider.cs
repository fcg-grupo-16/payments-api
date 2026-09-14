namespace Fcg.Payments.Payments;

/// <summary>Combina as <see cref="IPaymentRule"/> registradas para decidir o status do pagamento.</summary>
public interface IPaymentDecider
{
    /// <returns>
    /// <see cref="PaymentOutcome"/> com <see cref="PaymentDecision.Rejected"/> e o nome da PRIMEIRA
    /// regra que rejeitou; ou <see cref="PaymentDecision.Approved"/> com
    /// <see cref="PaymentOutcome.SemRegra"/> se nenhuma rejeitar.
    /// </returns>
    PaymentOutcome Decide(PaymentContext context);
}

public sealed class PaymentDecider(IEnumerable<IPaymentRule> rules) : IPaymentDecider
{
    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <c>FirstOrDefault</c> em vez de <c>Any</c>: o comportamento observável é idêntico (os dois
    /// fazem short-circuit na primeira rejeição), mas o <c>Any</c> descartava QUAL regra rejeitou.
    /// </para>
    /// <para>
    /// ⚠️ <b>Quando mais de uma regra rejeitaria, a creditada é a PRIMEIRA na ordem de registro no
    /// DI</b> (<c>Program.cs</c>: AmountLimit, BlockedUser, BlockedGame, RandomFailure). Isso é
    /// determinístico, não arbitrário — mas significa que reordenar os <c>AddSingleton</c> muda a
    /// distribuição do label <c>rule</c> nas métricas, sem alterar nenhuma decisão. Quem reordenar
    /// precisa saber disso.
    /// </para>
    /// </remarks>
    public PaymentOutcome Decide(PaymentContext context)
    {
        var rejeitadora = rules.FirstOrDefault(rule => rule.Rejects(context));

        return rejeitadora is null
            ? PaymentOutcome.Aprovado()
            : PaymentOutcome.Rejeitado(rejeitadora.Name);
    }
}
