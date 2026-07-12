using Microsoft.Extensions.Options;

namespace Fcg.Payments.Payments.Rules;

/// <summary>
/// Rejeita aleatoriamente na proporção <see cref="PaymentsOptions.RandomFailureRate"/> (0..1).
/// Com taxa 0 é determinístico (nunca rejeita por aqui). Aleatoriedade injetada (testável).
/// </summary>
public sealed class RandomFailureRule(IOptions<PaymentsOptions> options, IRandomSource random) : IPaymentRule
{
    public bool Rejects(PaymentContext context)
    {
        var rate = options.Value.RandomFailureRate;
        return rate > 0 && random.NextDouble() < rate;
    }
}
