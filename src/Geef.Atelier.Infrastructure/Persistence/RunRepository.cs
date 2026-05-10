using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence;

internal sealed class RunRepository(AtelierDbContext db) : IRunRepository
{
    /// <inheritdoc/>
    public async Task<RunEntity?> GetByIdAsync(Guid runId, CancellationToken cancellationToken = default)
        => await db.Runs.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RunEntity>> ListAsync(int limit, RunStatus? statusFilter, CancellationToken cancellationToken = default)
    {
        var q = db.Runs.AsNoTracking();
        if (statusFilter is { } s)
            q = q.Where(r => r.Status == s);
        return await q.OrderByDescending(r => r.CreatedAt).Take(limit).ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> RequestCancellationAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var affected = await db.Runs
            .Where(r => r.Id == runId
                     && (r.Status == RunStatus.Pending || r.Status == RunStatus.Running)
                     && !r.CancellationRequested)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CancellationRequested, true), cancellationToken);
        return affected > 0;
    }
}
