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
        // Config robusta: taxa não-finita ou <= 0 nunca rejeita; acima de 1 é clampada em 1
        // (evita que um valor errado como 20 vire "rejeita sempre" de forma surpreendente).
        if (!double.IsFinite(rate) || rate <= 0)
        {
            return false;
        }

        return random.NextDouble() < Math.Min(rate, 1d);
    }
}
