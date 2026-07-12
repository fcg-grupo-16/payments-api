using Microsoft.Extensions.Options;

namespace Fcg.Payments.Payments.Rules;

/// <summary>Rejeita pedidos de usuários em <see cref="PaymentsOptions.BlockedUserIds"/>.</summary>
public sealed class BlockedUserRule(IOptions<PaymentsOptions> options) : IPaymentRule
{
    public bool Rejects(PaymentContext context) => options.Value.BlockedUserIds.Contains(context.UserId);
}
