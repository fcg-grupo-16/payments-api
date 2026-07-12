namespace Fcg.Payments.Payments;

/// <summary>Dados de um pedido usados na avaliação das regras de decisão.</summary>
public sealed record PaymentContext(string UserId, string GameId, decimal Price);
