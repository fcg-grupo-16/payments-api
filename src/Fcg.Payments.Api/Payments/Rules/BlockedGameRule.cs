using Microsoft.Extensions.Options;

namespace Fcg.Payments.Payments.Rules;

/// <summary>Rejeita pedidos de jogos em <see cref="PaymentsOptions.BlockedGameIds"/>.</summary>
public sealed class BlockedGameRule(IOptions<PaymentsOptions> options) : IPaymentRule
{
    public bool Rejects(PaymentContext context) => options.Value.BlockedGameIds.Contains(context.GameId);
}
