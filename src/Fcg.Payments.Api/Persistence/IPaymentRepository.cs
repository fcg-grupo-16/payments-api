namespace Fcg.Payments.Persistence;

/// <summary>Persistência dos pagamentos processados (auditoria) na coleção <c>payments</c>.</summary>
public interface IPaymentRepository
{
    /// <summary>
    /// Grava o registro de auditoria do pagamento. Idempotente por <c>OrderId</c> (índice único):
    /// uma reentrega do mesmo pedido não gera documento duplicado nem lança.
    /// </summary>
    /// <returns><c>true</c> se um novo documento foi inserido; <c>false</c> se já existia (duplicado).</returns>
    Task<bool> RegistrarAsync(Payment payment, CancellationToken ct = default);

    /// <summary>Garante o índice único em <c>OrderId</c> (chamado no startup).</summary>
    Task GarantirIndicesAsync(CancellationToken ct = default);
}
