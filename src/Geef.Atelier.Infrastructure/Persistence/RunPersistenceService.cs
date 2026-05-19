using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence;

internal sealed class RunPersistenceService(AtelierDbContext db) : IRunPersistenceService
{
    private static readonly RunStatus[] TerminalStatuses =
        [RunStatus.Completed, RunStatus.Failed, RunStatus.Aborted];
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
        var affected = await db.Runs
            .Where(r => r.Id == runId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CrewSnapshot, snapshotJson), cancellationToken);
        if (affected == 0)
            throw new InvalidOperationException($"Run {runId} not found for snapshot update.");
    }

    /// <inheritdoc/>
    public async Task MarkRunFailedAsync(Guid runId, string errorMessage, CancellationToken cancellationToken = default)
    {
        await db.Runs
            .Where(r => r.Id == runId && !TerminalStatuses.Contains(r.Status))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status,       RunStatus.Failed)
                .SetProperty(r => r.ErrorMessage, errorMessage)
                .SetProperty(r => r.CompletedAt,  DateTimeOffset.UtcNow),
                cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Guid> CreateResumedRunAsync(
        string briefingText,
        string configJson,
        string? createdByUser,
        string? crewTemplateName,
        string? crewSnapshotJson,
        Guid parentRunId,
        string? seedDraftText,
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
            CostTotal        = 0m,
            ParentRunId      = parentRunId,
            SeedDraftText    = seedDraftText,
        };

        db.Runs.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        return run.Id;
    }
}
