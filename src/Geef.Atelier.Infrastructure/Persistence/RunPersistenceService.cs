using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence;

internal sealed class RunPersistenceService(AtelierDbContext db) : IRunPersistenceService
{
    public async Task<Guid> CreateRunAsync(
        string briefingText,
        string configJson,
        string? createdByUser = null,
        string? crewTemplateName = null,
        string? crewSnapshotJson = null,
        CancellationToken cancellationToken = default)
    {
        var run = new RunEntity
        {
            Id               = Guid.NewGuid(),
            CreatedAt        = DateTimeOffset.UtcNow,
            Status           = RunStatus.Pending,
            BriefingText     = briefingText,
            ConfigJson       = configJson,
            CreatedByUser    = createdByUser,
            CrewTemplateName = crewTemplateName,
            CrewSnapshot     = crewSnapshotJson,
            TokensTotal      = 0,
            CostTotal        = 0m
        };

        db.Runs.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        return run.Id;
    }

    /// <inheritdoc/>
    public async Task UpdateSnapshotAsync(Guid runId, string snapshotJson, CancellationToken cancellationToken = default)
    {
        await db.Runs
            .Where(r => r.Id == runId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CrewSnapshot, snapshotJson), cancellationToken);
    }
}
