using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Core.Persistence.Tools;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.Repositories;

internal sealed class ToolInvocationRepository(AtelierDbContext db) : IToolInvocationRepository
{
    /// <inheritdoc/>
    public async Task AddAsync(ToolInvocation invocation, CancellationToken ct = default)
    {
        db.ToolInvocations.Add(invocation);
        await db.SaveChangesAsync(ct);
        db.Entry(invocation).State = EntityState.Detached;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ToolInvocation>> GetByRunIdAsync(Guid runId, CancellationToken ct = default)
        => await db.ToolInvocations
            .AsNoTracking()
            .Where(t => t.RunId == runId)
            .OrderBy(t => t.Sequence)
            .ToListAsync(ct);
}
