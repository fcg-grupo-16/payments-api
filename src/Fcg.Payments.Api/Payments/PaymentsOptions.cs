namespace Fcg.Payments.Payments;

/// <summary>
/// Opções de configuração do processamento de pagamentos (seção "Payments").
/// </summary>
public sealed class PaymentsOptions
{
    public const string SectionName = "Payments";

    /// <summary>
    /// Valor máximo aprovado automaticamente. Pedidos acima deste valor são rejeitados.
    /// </summary>
    public decimal MaxApprovedAmount { get; init; } = 5000m;
}
