using Geef.Atelier.Infrastructure.Persistence;

namespace Geef.Atelier.Infrastructure.TemplateStudio;

internal sealed class EfAtomicTransactionFactory(AtelierDbContext db) : IAtomicTransactionFactory
{
    public async Task<IAtomicTransaction> BeginAsync(CancellationToken ct = default)
    {
        var txn = await db.Database.BeginTransactionAsync(ct);
        return new EfAtomicTransaction(txn);
    }

    private sealed class EfAtomicTransaction(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction inner) : IAtomicTransaction
    {
        public Task CommitAsync(CancellationToken ct = default)  => inner.CommitAsync(ct);
        public Task RollbackAsync(CancellationToken ct = default) => inner.RollbackAsync(ct);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
