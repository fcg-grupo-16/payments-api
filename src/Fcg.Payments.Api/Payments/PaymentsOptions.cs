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

    /// <summary>UserIds bloqueados — qualquer pedido desses usuários é rejeitado. Default: vazio.</summary>
    public IReadOnlyList<string> BlockedUserIds { get; init; } = [];

    /// <summary>GameIds bloqueados — qualquer pedido desses jogos é rejeitado. Default: vazio.</summary>
    public IReadOnlyList<string> BlockedGameIds { get; init; } = [];

    /// <summary>
    /// Taxa (0..1) de rejeição aleatória, para simular falhas. Default 0 (determinístico).
    /// </summary>
    public double RandomFailureRate { get; init; } = 0d;
}
