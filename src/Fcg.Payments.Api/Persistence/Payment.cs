using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Fcg.Payments.Persistence;

/// <summary>
/// Documento de auditoria de um pagamento processado (coleção <c>payments</c>).
/// Um documento por <see cref="OrderId"/> (índice único).
/// </summary>
public sealed class Payment
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    /// <summary>OrderId do pedido (Guid do OrderPlacedEvent, como string) — chave de negócio.</summary>
    public string OrderId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string GameId { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Price { get; set; }

    /// <summary>"Approved" ou "Rejected".</summary>
    public string Status { get; set; } = string.Empty;

    public DateTime ProcessedAt { get; set; }
}
