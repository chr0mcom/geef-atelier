using System.Globalization;
using System.Text.Json;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Persistence.Orders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Application.Runs;

internal sealed class RunService(
    IRunPersistenceService                persistence,
    IRunRepository                        repository,
    ILogger<RunService>                   logger,
    IRunWarningRepository                 warningRepository,
    IOrderRepository?                     orderRepository = null,
    IOptions<OrderOptions>?               orderOptions = null) : IRunService
{
    private static readonly JsonSerializerOptions SnapshotJsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RunWarningEntity>> ListRunWarningsAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
    {
        if (requestingUsername is not null && await GetRunAsync(runId, requestingUsername, cancellationToken) is null)
            return [];
        return await warningRepository.ListByRunAsync(runId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<RunEntity?> GetRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
    {
        var run = await repository.GetByIdAsync(runId, cancellationToken);
        if (run is null) return null;
        if (requestingUsername is not null && run.CreatedByUser != requestingUsername) return null;
        return run;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<RunEntity>> ListRunsAsync(int limit = 20, RunStatus? statusFilter = null, string? requestingUsername = null, CancellationToken cancellationToken = default)
    {
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be positive.");
        return repository.ListAsync(limit, statusFilter, requestingUsername, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> CancelRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
    {
        if (requestingUsername is not null)
        {
            var run = await repository.GetByIdAsync(runId, cancellationToken);
            if (run is null || run.CreatedByUser != requestingUsername) return false;
        }
        return await repository.RequestCancellationAsync(runId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<RunDetails?> GetRunDetailsAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
    {
        var details = await repository.GetDetailsAsync(runId, cancellationToken);
        if (details is null) return null;
        if (requestingUsername is not null && details.Run.CreatedByUser != requestingUsername) return null;
        return details;
    }

    /// <inheritdoc/>
    public async Task<Guid> ResumeRunAsync(ResumeOptions options, string? requestingUsername, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var details = await repository.GetDetailsAsync(options.ParentRunId, cancellationToken);
        if (details is null)
            throw new InvalidOperationException($"Run {options.ParentRunId} not found.");

        if (requestingUsername is not null && details.Run.CreatedByUser != requestingUsername)
            throw new InvalidOperationException(
                $"Run {options.ParentRunId} does not belong to user {requestingUsername}.");

        if (details.Run.Status is not (RunStatus.Aborted or RunStatus.Failed))
            throw new InvalidOperationException(
                $"Run {options.ParentRunId} is in status {details.Run.Status} and cannot be resumed. Only Aborted and Failed runs are resumable.");

        if (details.Run.ResumeRefusal() is { } refusal)
            throw new RunNotResumableException(refusal);

        if (!CrewSnapshot.IsUsableForRun(details.Run.CrewSnapshot))
            throw new RunNotResumableException(RunNotResumableException.NotPreparedMessage);

        string? seedDraftText = null;
        if (options.UseSeedDraft && !string.IsNullOrWhiteSpace(details.Run.FinalText))
        {
            seedDraftText = details.Run.FinalText;
        }
        else if (options.UseSeedDraft && details.Iterations.Count > 0)
        {
            seedDraftText = details.Iterations
                .OrderByDescending(i => i.Iteration.IterationNumber)
                .First()
                .Iteration.ArtifactText;
        }

        var snapshotJson = details.Run.CrewSnapshot;
        if (options.MaxIterationsOverride.HasValue && !string.IsNullOrEmpty(snapshotJson))
        {
            var snapshot = CrewSnapshot.Deserialize(snapshotJson);
            if (snapshot is not null)
            {
                var patched = snapshot with
                {
                    ConvergenceOverride = (snapshot.ConvergenceOverride ?? new ConvergencePolicyOverride(null, null, null, null))
                        with { MaxIterations = options.MaxIterationsOverride }
                };
                snapshotJson = JsonSerializer.Serialize(patched, SnapshotJsonOpts);
            }
        }

        if (details.Run.ChangesAnEarlierVersion() && details.Run.OrderId is { } changedOrderId && orderRepository is not null)
            return await ResumeChangeAsync(details.Run, changedOrderId, snapshotJson, cancellationToken);

        var resumedId = await persistence.CreateResumedRunAsync(
            details.Run.BriefingText,
            details.Run.ConfigJson,
            details.Run.CreatedByUser,
            details.Run.CrewTemplateName,
            null,
            options.ParentRunId,
            seedDraftText,
            details.Run.Kind,
            cancellationToken);

        if (details.Run.Kind != RunKind.Learning)
            await LinkToOrderAsync(resumedId, OrderEntry.Legacy, CancellationToken.None);

        await persistence.UpdateSnapshotAsync(resumedId, snapshotJson!, CancellationToken.None);
        return resumedId;
    }

    private async Task<Guid> ResumeChangeAsync(RunEntity failed, Guid orderId, string? snapshotJson, CancellationToken cancellationToken)
    {
        var orderRuns = await orderRepository!.ListRunsAsync(orderId, cancellationToken);
        var shown = OrderStateResolver.SelectShownRun(orderRuns)
                    ?? throw new RunNotResumableException(RevisionLines.NothingToChange);
        if ((shown.OrderVersion ?? 0) > (failed.OrderVersion ?? 0))
            throw new RunNotResumableException(RevisionLines.ResumeLaterVersion);
        var version = orderRuns.Where(r => r.Kind == RunKind.Standard).Max(r => r.OrderVersion ?? 0) + 1;
        var change = RevisionBrief.WithoutRetired(failed.RevisionHint);
        if (change.Length == 0)
            throw new RunNotResumableException(RevisionLines.ResumeRetired);
        var run = new RunEntity
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            Status = RunStatus.Pending,
            BriefingText = RevisionBrief.Compose(shown.BriefingText ?? string.Empty, change, version),
            ConfigJson = failed.ConfigJson,
            CreatedByUser = failed.CreatedByUser,
            CrewTemplateName = failed.CrewTemplateName,
            CrewSnapshot = snapshotJson,
            TokensTotal = 0,
            CostTotal = 0m,
            Kind = RunKind.Standard,
            OrderId = orderId,
            OrderVersion = version,
            ParentRunId = shown.Id,
            SeedDraftText = shown.FinalText,
            RevisionHint = change,
        };

        var maxVersions = (orderOptions?.Value ?? new OrderOptions()).MaxVersionsPerOrder;
        var (result, created) = await orderRepository.TryAddRevisionRunAsync(orderId, shown.Id, run, maxVersions, cancellationToken);
        logger.LogInformation("Run {RunId} resumed as version {Version}, a change of version {BaseVersion} of order {OrderId}: {Result}.",
            failed.Id, created, shown.OrderVersion, orderId, result);
        return result switch
        {
            RevisionStart.Created => run.Id,
            RevisionStart.Busy or RevisionStart.BaseNotShown => throw new RunNotResumableException(RevisionLines.Busy),
            RevisionStart.TooManyVersions => throw new RunNotResumableException(
                string.Format(CultureInfo.InvariantCulture, RevisionLines.TooManyVersionsFormat, maxVersions)),
            RevisionStart.Cancelled => throw new RunNotResumableException(RevisionLines.Stopped),
            _ => throw new RunNotResumableException(RevisionLines.NotFound),
        };
    }

    private async Task LinkToOrderAsync(Guid runId, OrderEntry entry, CancellationToken cancellationToken)
    {
        if (orderRepository is null)
            return;
        try
        {
            await orderRepository.LinkLegacyRunAsync(runId, entry, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError("Run {RunId} could not be linked to an order; it runs without one: {Error}", runId, ex.ToRedactedLogText());
        }
    }

    /// <inheritdoc/>
    public async Task<RunViewModel?> GetRunViewAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
    {
        var details = await repository.GetDetailsAsync(runId, cancellationToken);
        if (details is null)
            return null;
        if (requestingUsername is not null && details.Run.CreatedByUser != requestingUsername)
            return null;

        return new RunViewModel(
            details,
            CrewSnapshot.Deserialize(details.Run.CrewSnapshot),
            await warningRepository.ListByRunAsync(runId, cancellationToken));
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
    {
        var run = await GetRunAsync(runId, requestingUsername, cancellationToken);
        if (run is null) return false;

        if (run.Status is RunStatus.Pending or RunStatus.Running)
            throw new InvalidOperationException("Running or pending runs cannot be deleted. Cancel the run first.");

        var link = orderRepository is null ? null : await orderRepository.GetLinkForRunAsync(runId, cancellationToken);
        await repository.DeleteAsync(runId, cancellationToken);

        if (link is { OrderId: var orderId } && (await orderRepository!.ListRunsAsync(orderId, cancellationToken)).Count == 0)
            await orderRepository.DeleteAsync(orderId, cancellationToken);
        return true;
    }
}
