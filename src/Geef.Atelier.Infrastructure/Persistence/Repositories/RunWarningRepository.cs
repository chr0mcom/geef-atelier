using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.Repositories;

internal sealed class RunWarningRepository(AtelierDbContext db) : IRunWarningRepository
{
    public async Task<IReadOnlyList<RunWarningEntity>> ListByRunAsync(Guid runId, CancellationToken ct = default)
        => await db.RunWarnings
            .AsNoTracking()
            .Where(w => w.RunId == runId)
            .OrderBy(w => w.CreatedAt)
            .ThenBy(w => w.Id)
            .ToListAsync(ct);
}
