using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence;

internal sealed class RunArtifactRepository(AtelierDbContext db) : IRunArtifactRepository
{
    public async Task<IReadOnlyList<RunArtifact>> ListByRunAsync(Guid runId, CancellationToken ct)
        => await db.RunArtifacts
            .AsNoTracking()
            .Where(a => a.RunId == runId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);

    public Task<RunArtifact?> GetByIdAsync(Guid artifactId, CancellationToken ct)
        => db.RunArtifacts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == artifactId, ct);

    public async Task<RunArtifact> CreateAsync(RunArtifact artifact, CancellationToken ct)
    {
        db.RunArtifacts.Add(artifact);
        await db.SaveChangesAsync(ct);
        db.Entry(artifact).State = EntityState.Detached;
        return artifact;
    }

    public async Task DeleteByRunAsync(Guid runId, CancellationToken ct)
    {
        await db.RunArtifacts
            .Where(a => a.RunId == runId)
            .ExecuteDeleteAsync(ct);
    }
}
