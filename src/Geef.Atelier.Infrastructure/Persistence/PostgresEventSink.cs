using System.Text.Json;
using System.Text.Json.Serialization;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Notifications;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Sdk.Context;
using Geef.Sdk.Events;
using Geef.Sdk.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Persistence;

internal sealed class PostgresEventSink(
    Guid                 atelierRunId,
    IServiceScopeFactory scopeFactory,
    IRunNotifier         notifier,
    ILogger              logger) : IGeefEventSink
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        WriteIndented    = false
    };

    private volatile IRunContext? _lastExecutionContext;

    public async ValueTask PublishAsync(IGeefEvent geefEvent, CancellationToken cancellationToken)
    {
        try
        {
            await HandleEventAsync(geefEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PostgresEventSink failed for event {EventType} on run {RunId}",
                geefEvent.GetType().Name, atelierRunId);
        }
    }

    private async Task HandleEventAsync(IGeefEvent geefEvent, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtelierDbContext>();

        await PersistRawEventAsync(db, geefEvent, ct);

        switch (geefEvent)
        {

            case PipelineStartedEvent started:
                await db.Runs
                    .Where(r => r.Id == atelierRunId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.StartedAt, r => r.StartedAt ?? started.Timestamp), ct);
                break;

            case ExecutionCompletedEvent exec:
                _lastExecutionContext = exec.Result.UpdatedContext;

                var draft = exec.Result.UpdatedContext.TryGet(AtelierContextKeys.CurrentDraft, out var d) ? (d ?? string.Empty) : string.Empty;
                await db.Iterations.AddAsync(new IterationEntity
                {
                    Id              = Guid.NewGuid(),
                    RunId           = atelierRunId,
                    IterationNumber = exec.Iteration,
                    ArtifactText    = draft,
                    CreatedAt       = exec.Timestamp
                }, ct);
                await db.SaveChangesAsync(ct);

                if (exec.Result.UpdatedContext.TryGet(AtelierContextKeys.TokenUsage, out var usage) && usage is not null)
                {
                    var delta = usage.InputTokens + usage.OutputTokens;
                    await db.Runs
                        .Where(r => r.Id == atelierRunId)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(r => r.TokensTotal, r => r.TokensTotal + delta), ct);
                }
                break;

            case EvaluationApprovedEvent approved:
                await PersistFindingsAsync(db, approved.Aggregate.AllFindings, approved.Iteration, ct);
                break;

            case EvaluationRejectedEvent rejected:
                await PersistFindingsAsync(db, rejected.Aggregate.AllFindings, rejected.Iteration, ct);
                break;

            case PipelineCompletedEvent:
                var finalText = _lastExecutionContext?.TryGet(AtelierContextKeys.CurrentDraft, out var ft) == true ? ft : null;
                await db.Runs
                    .Where(r => r.Id == atelierRunId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.Status,      RunStatus.Completed)
                        .SetProperty(r => r.CompletedAt, geefEvent.Timestamp)
                        .SetProperty(r => r.FinalText,   finalText), ct);
                break;

            case PipelineFailedEvent failed:
                var isAbort    = failed.Reason == ConvergenceDecision.AbortCriticalBlocker;
                var failStatus = isAbort ? RunStatus.Aborted : RunStatus.Failed;
                var errorMsg   = isAbort
                    ? "Aborted due to critical reviewer finding"
                    : $"Pipeline failed: {failed.Reason}";
                await db.Runs
                    .Where(r => r.Id == atelierRunId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.Status,       failStatus)
                        .SetProperty(r => r.CompletedAt,  geefEvent.Timestamp)
                        .SetProperty(r => r.ErrorMessage, errorMsg), ct);

                // For critical aborts, EvaluationRejectedEvent is not fired by the SDK —
                // findings live in the last iteration's evaluation history.
                if (isAbort)
                {
                    var lastRecord = failed.History.Records.LastOrDefault();
                    if (lastRecord is not null)
                        await PersistFindingsAsync(db, lastRecord.EvaluationResult.AllFindings, lastRecord.Iteration, ct);
                }
                break;
        }

        try
        {
            await notifier.NotifyRunUpdatedAsync(atelierRunId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "IRunNotifier failed for run {RunId}; UI update skipped", atelierRunId);
        }
    }

    private async Task PersistFindingsAsync(
        AtelierDbContext          db,
        IReadOnlyList<Geef.Sdk.Results.Finding> findings,
        int                       iterationNumber,
        CancellationToken         ct)
    {
        var iteration = await db.Iterations
            .Where(i => i.RunId == atelierRunId && i.IterationNumber == iterationNumber)
            .FirstOrDefaultAsync(ct);

        if (iteration is null) return;

        foreach (var finding in findings)
        {
            await db.Findings.AddAsync(new FindingEntity
            {
                Id           = Guid.NewGuid(),
                IterationId  = iteration.Id,
                ReviewerName = finding.ReviewerName,
                Severity     = finding.Severity.ToAtelierSeverity(),
                Message      = finding.Message,
                CreatedAt    = DateTimeOffset.UtcNow
            }, ct);
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task PersistRawEventAsync(AtelierDbContext db, IGeefEvent geefEvent, CancellationToken ct)
    {
        string payload;
        try
        {
            payload = JsonSerializer.Serialize(geefEvent, geefEvent.GetType(), SerializerOptions);
        }
        catch (Exception ex)
        {
            payload = JsonSerializer.Serialize(new { type = geefEvent.GetType().Name, error = ex.Message });
        }

        await db.Events.AddAsync(new EventEntity
        {
            Id          = 0,
            RunId       = atelierRunId,
            EventType   = geefEvent.GetType().Name,
            PayloadJson = payload,
            CreatedAt   = geefEvent.Timestamp
        }, ct);
        await db.SaveChangesAsync(ct);
    }
}
