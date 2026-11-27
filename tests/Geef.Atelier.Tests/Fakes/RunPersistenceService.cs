using Geef.Atelier.Core.Domain;
using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Tests;

/// <summary>
/// Stores runs without an order, for tests that start the orchestrator or the event sink directly. The application itself only creates
/// runs as part of an order.
/// </summary>
internal sealed class RunPersistenceService(AtelierDbContext db)
{
    public async Task<Guid> CreateRunAsync(
        string briefingText,
        string? createdByUser = null,
        string? crewTemplateName = null,
        string? crewSnapshotJson = null,
        CancellationToken cancellationToken = default)
    {
        var run = new RunEntity
        {
            Id                      = Guid.NewGuid(),
            CreatedAt               = DateTimeOffset.UtcNow,
            Status                  = RunStatus.Pending,
            BriefingText            = briefingText,
            CreatedByUser           = createdByUser,
            CrewTemplateName        = crewTemplateName,
            CrewSnapshot            = crewSnapshotJson,
            TokensTotal             = 0,
        };

        db.Runs.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        return run.Id;
    }

    /// <summary>Replaces the snapshot of a run; a run without a snapshot is not picked up, so saving one releases it.</summary>
    public async Task UpdateSnapshotAsync(Guid runId, string snapshotJson, CancellationToken cancellationToken = default)
    {
        var affected = await db.Runs
            .Where(r => r.Id == runId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CrewSnapshot, snapshotJson), cancellationToken);
        if (affected == 0)
            throw new InvalidOperationException($"Run {runId} not found for snapshot update.");
    }
}
