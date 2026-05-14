using System.Collections.Concurrent;
using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Notifications;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Infrastructure.Pricing;
using Geef.Sdk.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Web.Services;

/// <summary>BackgroundService that polls for Pending runs and dispatches them to the Geef pipeline.</summary>
internal sealed class RunOrchestratorService(
    IServiceScopeFactory                scopeFactory,
    ILlmClientResolver                  llmClientResolver,
    IRunNotifier                        runNotifier,
    IOptions<OrchestratorOptions>       options,
    IOptions<ConvergenceOptions>        convergenceOptions,
    ILoggerFactory                      loggerFactory,
    ILogger<RunOrchestratorService>     logger,
    IGroundingProviderFactory?          groundingProviderFactory = null,
    IPricingCatalog?                    pricingCatalog = null,
    IOptions<CostTrackingOptions>?      costTrackingOptions = null) : BackgroundService
{
    private readonly OrchestratorOptions _opts = options.Value;
    private readonly SemaphoreSlim _slots = new(options.Value.MaxConcurrentRuns, options.Value.MaxConcurrentRuns);
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _runCts   = new();
    private readonly ConcurrentDictionary<Guid, Task>                    _runTasks  = new();

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RecoverCrashedRunsAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Crash recovery on startup failed; stale Running runs were not reset.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAndDispatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Polling loop iteration failed; continuing.");
            }

            try
            {
                await Task.Delay(_opts.PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // Drain in-flight runs so StopAsync does not return until every ProcessRunAsync has written
        // its final status to the database.
        if (!_runTasks.IsEmpty)
            await Task.WhenAll(_runTasks.Values.ToArray());
    }

    /// <summary>
    /// On startup, marks any runs left in Running state as Failed — they were interrupted by a crash or restart.
    /// </summary>
    private async Task RecoverCrashedRunsAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtelierDbContext>();

        var affected = await db.Runs
            .Where(r => r.Status == RunStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status,       RunStatus.Failed)
                .SetProperty(r => r.ErrorMessage, "Service restarted")
                .SetProperty(r => r.CompletedAt,  DateTimeOffset.UtcNow),
                stoppingToken);

        if (affected > 0)
            logger.LogWarning("Crash recovery: marked {Count} stale Running runs as Failed.", affected);
    }

    /// <summary>
    /// Loads at most <c>_slots.CurrentCount</c> Pending runs from the database and dispatches each
    /// after an atomic claim to prevent double-processing in multi-instance deployments.
    /// </summary>
    private async Task PollAndDispatchAsync(CancellationToken stoppingToken)
    {
        var slotsAvailable = _slots.CurrentCount;
        if (slotsAvailable == 0)
            return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtelierDbContext>();

        var runs = await db.Runs
            .AsNoTracking()
            .Where(r => r.Status == RunStatus.Pending)
            .OrderBy(r => r.CreatedAt)
            .Take(slotsAvailable)
            .ToListAsync(stoppingToken);

        foreach (var run in runs)
        {
            // Atomic claim: transitions Pending → Running; affectedRows=0 means another instance already picked it up.
            var claimed = await db.Runs
                .Where(r => r.Id == run.Id && r.Status == RunStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, RunStatus.Running),
                    stoppingToken);

            if (claimed == 0)
                continue;

            await _slots.WaitAsync(stoppingToken);
            var runTask = Task.Run(() => ProcessRunAsync(run, stoppingToken), CancellationToken.None);
            _runTasks[run.Id] = runTask;
        }
    }

    /// <summary>
    /// Executes the full Geef pipeline for a single claimed run. Releases the semaphore slot when done.
    /// Also starts a cancellation watcher that polls the DB flag and cancels the run CTS if set.
    /// </summary>
    private async Task ProcessRunAsync(RunEntity run, CancellationToken stoppingToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _runCts[run.Id] = cts;

        // Watcher polls DB flag; signals cts if CancellationRequested=true.
        var watcherTask = Task.Run(() => WatchCancellationAsync(run.Id, cts), CancellationToken.None);

        try
        {
            var sinkLogger = loggerFactory.CreateLogger($"PostgresEventSink[{run.Id}]");
            var sink       = new PostgresEventSink(run.Id, scopeFactory, runNotifier, sinkLogger);
            var snapshot   = ResolveSnapshot(run);

            var costTrackingEnabled = costTrackingOptions?.Value.Enabled ?? false;
            var accumulator = costTrackingEnabled ? new RunCostAccumulator() : null;

            await using var scope = scopeFactory.CreateAsyncScope();
            var consultations = scope.ServiceProvider.GetRequiredService<IAdvisorConsultationRepository>();

            var runner = AtelierPipelineFactory.Build(
                snapshot, llmClientResolver, convergenceOptions,
                consultationRepository: consultations,
                runId: run.Id,
                loggerFactory: loggerFactory,
                additionalSinks: [sink],
                groundingProviderFactory: groundingProviderFactory,
                pricingCatalog: pricingCatalog,
                costAccumulator: accumulator);

            try
            {
                await runner.RunAsync(run.BriefingText, cts.Token);
                if (accumulator is not null)
                    await FinalizeRunCostsAsync(run.Id, accumulator, cts.Token);
            }
            catch (ConvergenceFailedException convergenceEx)
            {
                logger.LogWarning(convergenceEx,
                    "Run {RunId} failed to converge; checking for OnConvergenceFailure advisors.", run.Id);

                var retried = await TryConvergenceFailureRetryAsync(
                    run, snapshot, cts.Token);

                if (!retried)
                {
                    // No retry was performed — propagate so the outer handler marks it Failed.
                    throw;
                }
                // Retry ran: status was written inside TryConvergenceFailureRetryAsync.
                try { await runNotifier.NotifyRunUpdatedAsync(run.Id); } catch { /* best-effort */ }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Do not pass the already-cancelled stoppingToken — use None so the DB write completes.
            await OverrideToAbortedAsync(run.Id, "Service stopping");
            try { await runNotifier.NotifyRunUpdatedAsync(run.Id); } catch { /* best-effort */ }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // User-initiated cancellation: sink may have written Failed; override to Aborted.
            await OverrideToAbortedAsync(run.Id, "Cancelled by user");
            try { await runNotifier.NotifyRunUpdatedAsync(run.Id); } catch { /* best-effort */ }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Run {RunId} pipeline threw unhandled exception.", run.Id);
            await MarkRunFailedAsync(run.Id, SanitizeErrorMessage(ex));
            try { await runNotifier.NotifyRunUpdatedAsync(run.Id); } catch { /* best-effort */ }
        }
        finally
        {
            // Stop the watcher cleanly before releasing resources.
            if (!cts.IsCancellationRequested)
                cts.Cancel();
            try { await watcherTask; } catch { /* watcher exits via OCE; swallow */ }

            _runCts.TryRemove(run.Id, out _);
            cts.Dispose();
            _slots.Release();
            _runTasks.TryRemove(run.Id, out _); // last: keeps task in dict until fully done so drain can await it
        }
    }

    /// <summary>
    /// Polls the database for the <c>CancellationRequested</c> flag on the given run.
    /// Cancels <paramref name="cts"/> as soon as the flag is found to be true.
    /// </summary>
    private async Task WatchCancellationAsync(Guid runId, CancellationTokenSource cts)
    {
        while (!cts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_opts.CancellationPollingInterval, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AtelierDbContext>();

                var requested = await db.Runs
                    .Where(r => r.Id == runId)
                    .Select(r => r.CancellationRequested)
                    .FirstOrDefaultAsync(cts.Token);

                if (requested)
                {
                    cts.Cancel();
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Cancellation watcher for run {RunId} encountered an error; will retry.", runId);
            }
        }
    }

    /// <summary>
    /// Attempts a convergence-failure recovery pass if the run has
    /// <see cref="AdvisorTrigger.OnConvergenceFailure"/> advisors and has not already retried.
    /// Consults each advisor, assembles their outputs into an advisor-context block, rebuilds the
    /// pipeline via <see cref="AtelierPipelineFactory.BuildWithAdvisorContext"/>, and runs it.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if a recovery pass was started (regardless of whether it converges);
    /// <see langword="false"/> if no retry is possible (no advisors or single-retry cap exhausted).
    /// </returns>
    private async Task<bool> TryConvergenceFailureRetryAsync(
        RunEntity         run,
        CrewSnapshot      snapshot,
        CancellationToken ct)
    {
        // 1. Check for OnConvergenceFailure advisors.
        var convergenceAdvisors = snapshot.Advisors
            .Where(a => a.Trigger == AdvisorTrigger.OnConvergenceFailure)
            .ToList();

        if (convergenceAdvisors.Count == 0)
        {
            logger.LogDebug("Run {RunId}: no OnConvergenceFailure advisors; skipping retry.", run.Id);
            return false;
        }

        // 2. Enforce single-retry cap: load AdvisorRetryAttempted from DB.
        bool alreadyRetried;
        await using (var capScope = scopeFactory.CreateAsyncScope())
        {
            var capDb = capScope.ServiceProvider.GetRequiredService<AtelierDbContext>();
            alreadyRetried = await capDb.Runs
                .Where(r => r.Id == run.Id)
                .Select(r => r.AdvisorRetryAttempted)
                .FirstOrDefaultAsync(ct);
        }

        if (alreadyRetried)
        {
            logger.LogWarning(
                "Run {RunId}: convergence-failure retry already attempted; not retrying again.", run.Id);
            return false;
        }

        // 3. Mark retry as attempted — prevents a second retry on a subsequent convergence failure.
        await using (var markScope = scopeFactory.CreateAsyncScope())
        {
            var markDb = markScope.ServiceProvider.GetRequiredService<AtelierDbContext>();
            await markDb.Runs
                .Where(r => r.Id == run.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.AdvisorRetryAttempted, true), ct);
        }

        logger.LogInformation(
            "Run {RunId}: starting convergence-failure recovery pass with {Count} advisor(s).",
            run.Id, convergenceAdvisors.Count);

        // 4. Consult each OnConvergenceFailure advisor and collect their outputs.
        var advisorOutputs = new List<string>(convergenceAdvisors.Count);
        await using (var advisorScope = scopeFactory.CreateAsyncScope())
        {
            var consultationRepo = advisorScope.ServiceProvider
                .GetRequiredService<IAdvisorConsultationRepository>();

            foreach (var profile in convergenceAdvisors)
            {
                var advisor      = new ProfileBasedAdvisor(profile, llmClientResolver, consultationRepo);
                var consultation = await advisor.ConsultAsync(run.Id, -1, run.BriefingText, ct);
                advisorOutputs.Add(
                    $"## {profile.DisplayName} ({profile.Mode})\n{consultation.Output}");
            }
        }

        var advisorBlock =
            $"[Convergence failure recovery — advisor consultations]\n\n" +
            $"{string.Join("\n\n", advisorOutputs)}\n\n" +
            $"[End of advisor consultations]";

        // 5. Build and run a new pipeline with the advisor context injected.
        var retrySinkLogger = loggerFactory.CreateLogger($"PostgresEventSink[{run.Id}]#retry");
        var retrySink       = new PostgresEventSink(run.Id, scopeFactory, runNotifier, retrySinkLogger);

        await using var retryScope = scopeFactory.CreateAsyncScope();
        var retryConsultations = retryScope.ServiceProvider
            .GetRequiredService<IAdvisorConsultationRepository>();

        var costTrackingEnabled = costTrackingOptions?.Value.Enabled ?? false;
        var retryAccumulator = costTrackingEnabled ? new RunCostAccumulator() : null;

        var retryRunner = AtelierPipelineFactory.BuildWithAdvisorContext(
            snapshot,
            llmClientResolver,
            convergenceOptions,
            advisorBlock,
            consultationRepository: retryConsultations,
            runId: run.Id,
            loggerFactory: loggerFactory,
            additionalSinks: [retrySink],
            groundingProviderFactory: groundingProviderFactory,
            pricingCatalog: pricingCatalog,
            costAccumulator: retryAccumulator);

        // The retry pipeline writes its own status via PostgresEventSink just like the main run.
        // ConvergenceFailedException from the retry is intentionally not caught here — it propagates
        // to the outer ProcessRunAsync handler which will mark the run as Failed.
        await retryRunner.RunAsync(run.BriefingText, ct);
        if (retryAccumulator is not null)
            await FinalizeRunCostsAsync(run.Id, retryAccumulator, ct);

        return true;
    }

    private static CrewSnapshot ResolveSnapshot(RunEntity run)
    {
        var deserialized = CrewSnapshot.Deserialize(run.CrewSnapshot);
        if (deserialized is not null)
            return deserialized;

        // Defensive fallback: pre-PS-5 runs have no snapshot; reconstruct from code constants.
        return new CrewSnapshot(
            SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
            TemplateName: SystemCrew.KlassikTemplateName,
            Executor: SystemCrew.DefaultExecutorProfile,
            Reviewers: [SystemCrew.BriefingFidelityProfile, SystemCrew.ClarityProfile],
            EvaluationStrategy: EvaluationStrategy.Parallel,
            ConvergenceOverride: null,
            Advisors: Array.Empty<AdvisorProfile>());
    }

    /// <summary>
    /// Marks a run that is still in Running state as Failed with the given error message.
    /// Called when the Geef pipeline throws an unhandled exception that the SDK did not convert
    /// to a <c>PipelineFailedEvent</c> (e.g. <see cref="HttpRequestException"/> from the LLM provider).
    /// </summary>
    private async Task MarkRunFailedAsync(Guid runId, string message, CancellationToken ct = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AtelierDbContext>();

            await db.Runs
                .Where(r => r.Id == runId && r.Status == RunStatus.Running)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status,       RunStatus.Failed)
                    .SetProperty(r => r.ErrorMessage, message)
                    .SetProperty(r => r.CompletedAt,  DateTimeOffset.UtcNow),
                    ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to mark run {RunId} as Failed.", runId);
        }
    }

    /// <summary>
    /// Produces a sanitized, user-visible error message from a pipeline exception.
    /// Walks the full inner-exception chain so that exceptions wrapped by the Geef SDK
    /// are handled by the most-specific matching rule.
    /// Sensitive details (API keys, full stack traces, provider URLs) are never included.
    /// </summary>
    internal static string SanitizeErrorMessage(Exception ex)
    {
        var current = ex;
        while (current is not null)
        {
            if (current is HttpRequestException { StatusCode: { } code })
                return (int)code switch
                {
                    400 => "LLM provider returned an invalid response (HTTP 400). Check model name and configuration.",
                    401 => "LLM provider authentication failed.",
                    403 => "LLM provider access denied.",
                    429 => "LLM provider rate limit exceeded. Retry later.",
                    >= 500 => "LLM provider temporarily unavailable. Retry later.",
                    _ => $"LLM provider returned an error (HTTP {(int)code})."
                };

            if (current is HttpRequestException)
                return "LLM provider request failed. Check connectivity and configuration.";

            if (current is TaskCanceledException)
                return "LLM provider request timed out.";

            current = current.InnerException;
        }

        return $"Pipeline execution failed: {ex.Message.Split('\n')[0].Trim()}";
    }

    /// <summary>
    /// Forces a run that is still Running or Failed into Aborted state with a reason message.
    /// Used when the service is stopping mid-execution or when the user cancels the run.
    /// </summary>
    private async Task OverrideToAbortedAsync(Guid runId, string reason, CancellationToken ct = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AtelierDbContext>();

            await db.Runs
                .Where(r => r.Id == runId && (r.Status == RunStatus.Running || r.Status == RunStatus.Failed))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status,       RunStatus.Aborted)
                    .SetProperty(r => r.ErrorMessage, reason)
                    .SetProperty(r => r.CompletedAt,  DateTimeOffset.UtcNow),
                    ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to mark run {RunId} as Aborted during shutdown.", runId);
        }
    }

    /// <summary>
    /// Persists per-actor cost records, aggregates them at iteration level, and updates the run totals.
    /// Called after a successful pipeline run when cost tracking is enabled.
    /// </summary>
    private async Task FinalizeRunCostsAsync(Guid runId, ICostAccumulator accumulator, CancellationToken ct)
    {
        try
        {
            var pending = accumulator.Flush();
            if (pending.Count == 0) return;

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AtelierDbContext>();

            // Load iterations to map IterationNumber → IterationId
            var iterations = await db.Iterations
                .Where(i => i.RunId == runId)
                .OrderBy(i => i.IterationNumber)
                .ToListAsync(ct);

            var iterByNumber = iterations.ToDictionary(i => i.IterationNumber, i => i);

            // Persist individual actor cost records
            var costEntities = new List<IterationActorCostEntity>();
            foreach (var cost in pending)
            {
                if (!iterByNumber.TryGetValue(cost.IterationNumber, out var iter))
                    continue;

                costEntities.Add(new IterationActorCostEntity
                {
                    Id           = Guid.NewGuid(),
                    IterationId  = iter.Id,
                    ActorType    = cost.ActorType,
                    ActorName    = cost.ActorName,
                    ModelName    = cost.ModelName,
                    InputTokens  = cost.InputTokens,
                    OutputTokens = cost.OutputTokens,
                    CostEur      = cost.CostEur,
                    CreatedAt    = DateTimeOffset.UtcNow
                });
            }

            db.IterationActorCosts.AddRange(costEntities);
            await db.SaveChangesAsync(ct);

            // Aggregate costs per iteration and update iteration rows
            foreach (var iter in iterations)
            {
                var iterCosts = costEntities.Where(c => c.IterationId == iter.Id).ToList();
                if (iterCosts.Count == 0) continue;

                var execCosts     = iterCosts.Where(c => c.ActorType == ActorType.Executor).ToList();
                var reviewerCosts = iterCosts.Where(c => c.ActorType == ActorType.Reviewer).ToList();
                var advisorCosts  = iterCosts.Where(c => c.ActorType == ActorType.Advisor).ToList();

                await db.Iterations
                    .Where(i => i.Id == iter.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(i => i.ExecutorInputTokens,    execCosts.Sum(c => c.InputTokens))
                        .SetProperty(i => i.ExecutorOutputTokens,   execCosts.Sum(c => c.OutputTokens))
                        .SetProperty(i => i.ExecutorCostEur,        execCosts.Any(c => c.CostEur.HasValue) ? execCosts.Sum(c => c.CostEur ?? 0m) : (decimal?)null)
                        .SetProperty(i => i.ReviewersTotalCostEur,  reviewerCosts.Any(c => c.CostEur.HasValue) ? reviewerCosts.Sum(c => c.CostEur ?? 0m) : (decimal?)null)
                        .SetProperty(i => i.AdvisorsTotalCostEur,   advisorCosts.Any(c => c.CostEur.HasValue) ? advisorCosts.Sum(c => c.CostEur ?? 0m) : (decimal?)null),
                        ct);
            }

            // Aggregate on run level
            var llmCostEur = costEntities.Any(c => c.CostEur.HasValue)
                ? costEntities.Sum(c => c.CostEur ?? 0m)
                : (decimal?)null;

            // Sum grounding costs from GroundingConsultations
            var groundingCostEur = await db.GroundingConsultations
                .Where(g => g.RunId == runId && g.CostEur.HasValue)
                .SumAsync(g => g.CostEur!.Value, ct);
            var groundingCostNullable = groundingCostEur > 0 ? groundingCostEur : (decimal?)null;

            var totalCostEur = (llmCostEur ?? 0m) + (groundingCostNullable ?? 0m);
            var totalNullable = totalCostEur > 0 ? totalCostEur : (decimal?)null;

            await db.Runs
                .Where(r => r.Id == runId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.LlmCostEur,      llmCostEur)
                    .SetProperty(r => r.GroundingCostEur, groundingCostNullable)
                    .SetProperty(r => r.TotalCostEur,     totalNullable),
                    ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to finalize cost tracking for run {RunId}.", runId);
        }
    }
}
