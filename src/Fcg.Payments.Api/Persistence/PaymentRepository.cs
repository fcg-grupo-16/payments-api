using MongoDB.Driver;

namespace Fcg.Payments.Persistence;

public sealed class PaymentRepository : IPaymentRepository
{
    private readonly IMongoCollection<Payment> _payments;

    public PaymentRepository(IMongoDatabase database)
    {
        _payments = database.GetCollection<Payment>("payments");
    }

    public async Task<bool> RegistrarAsync(Payment payment, CancellationToken ct = default)
    {
        try
        {
            await _payments.InsertOneAsync(payment, options: null, ct);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Reentrega do mesmo OrderId (índice único): já auditado — não é erro.
            return false;
        }
    }

    public async Task GarantirIndicesAsync(CancellationToken ct = default)
    {
        var indice = new CreateIndexModel<Payment>(
            Builders<Payment>.IndexKeys.Ascending(p => p.OrderId),
            new CreateIndexOptions { Unique = true, Name = "ux_orderId" });

        await _payments.Indexes.CreateOneAsync(indice, cancellationToken: ct);
    }
}
