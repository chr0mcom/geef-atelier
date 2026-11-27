namespace Geef.Atelier.Infrastructure.Persistence;

/// <summary>Factory that opens a database transaction scoped to a single materialization operation.</summary>
internal interface IAtomicTransactionFactory
{
    Task<IAtomicTransaction> BeginAsync(CancellationToken ct = default);
}

/// <summary>A scoped database transaction for one materialization.</summary>
internal interface IAtomicTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}
