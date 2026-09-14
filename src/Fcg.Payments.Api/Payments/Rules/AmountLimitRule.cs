using Microsoft.Extensions.Options;

namespace Fcg.Payments.Payments.Rules;

/// <summary>Rejeita quando o valor do pedido excede <see cref="PaymentsOptions.MaxApprovedAmount"/>.</summary>
public sealed class AmountLimitRule(IOptions<PaymentsOptions> options) : IPaymentRule
{
    /// <inheritdoc />
    public string Name => "AmountLimit";

    public bool Rejects(PaymentContext context) => context.Price > options.Value.MaxApprovedAmount;
}
