using System.Collections.Concurrent;
using Geef.Atelier.Application.Composition;
using Geef.Atelier.Application.Crew.Finalizers;
using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Application.Grounding;
using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Notifications;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Composition;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Infrastructure.Pricing;
using Geef.Sdk;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    IOptions<CostTrackingOptions>?      costTrackingOptions = null,
    IFinalizerExecutorFactory?          finalizerExecutorFactory = null) : BackgroundService
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
            var consultations              = scope.ServiceProvider.GetRequiredService<IAdvisorConsultationRepository>();
            var groundingRefiner           = scope.ServiceProvider.GetService<IGroundingRefiner>();
            var groundingConsultationRepo  = scope.ServiceProvider.GetService<IGroundingConsultationRepository>();
            var toolBackedGroundingProvider = scope.ServiceProvider.GetService<Geef.Atelier.Infrastructure.Grounding.ToolBackedGroundingProvider>();

            GeefPipelineRunner<FinalizedDocument> runner;
            var isCompositionRun = run.Kind == RunKind.CrewComposition
                || snapshot.TemplateName == SystemCrew.CrewComposerTemplateName;
            if (isCompositionRun)
            {
                var crewSpecValidator = scope.ServiceProvider.GetRequiredService<ICrewSpecValidator>();
                var validatorReviewer = new CrewSpecValidatorReviewer(crewSpecValidator);
                var composerExecutor  = ActivatorUtilities.CreateInstance<CrewComposerExecutor>(
                    scope.ServiceProvider, snapshot.Executor);

                runner = AtelierPipelineFactory.Build(
                    snapshot, llmClientResolver, convergenceOptions,
                    consultationRepository: consultations,
                    runId: run.Id,
                    loggerFactory: loggerFactory,
                    additionalSinks: [sink],
                    groundingProviderFactory: groundingProviderFactory,
                    pricingCatalog: pricingCatalog,
                    costAccumulator: accumulator,
                    groundingRefiner: groundingRefiner,
                    groundingConsultationRepository: groundingConsultationRepo,
                    customExecutionStep: composerExecutor,
                    specialReviewerResolver: profile =>
                        profile.Name == "crew-spec-validator" ? validatorReviewer : null,
                    toolBackedGroundingProvider: toolBackedGroundingProvider);
            }
            else
            {
                runner = run.SeedDraftText is not null
                    ? AtelierPipelineFactory.BuildWithSeedDraft(
                        snapshot, llmClientResolver, convergenceOptions, run.SeedDraftText,
                        consultationRepository: consultations,
                        runId: run.Id,
                        loggerFactory: loggerFactory,
                        additionalSinks: [sink],
                        groundingProviderFactory: groundingProviderFactory,
                        pricingCatalog: pricingCatalog,
                        costAccumulator: accumulator,
                        groundingRefiner: groundingRefiner,
                        groundingConsultationRepository: groundingConsultationRepo,
                        toolBackedGroundingProvider: toolBackedGroundingProvider)
                    : AtelierPipelineFactory.Build(
                        snapshot, llmClientResolver, convergenceOptions,
                        consultationRepository: consultations,
                        runId: run.Id,
                        loggerFactory: loggerFactory,
                        additionalSinks: [sink],
                        groundingProviderFactory: groundingProviderFactory,
                        pricingCatalog: pricingCatalog,
                        costAccumulator: accumulator,
                        groundingRefiner: groundingRefiner,
                        groundingConsultationRepository: groundingConsultationRepo,
                        toolBackedGroundingProvider: toolBackedGroundingProvider);
            }

            var result = await runner.RunAsync(run.BriefingText, cts.Token);

            if (result.Success)
            {
                if (accumulator is not null)
                    await FinalizeRunCostsAsync(run.Id, accumulator, cts.Token);
                await ExecuteFinalizationAsync(run.Id, snapshot, cts.Token);
            }
            else
            {
                // Non-convergence best-effort path (standard and seed-draft runs only).
                // The SDK already finalized the iteration with the fewest blocking findings and
                // published PipelineCompletedEvent; result.Output.Markdown is the best-effort text.
                logger.LogWarning(
                    "Run {RunId} did not converge (stop: {Reason}, degraded iterations: {Degraded}). Surfacing best-effort draft.",
                    run.Id, result.StopReason, result.DegradedIterations);

                if (accumulator is not null)
                    await FinalizeRunCostsAsync(run.Id, accumulator, cts.Token);

                var shouldRunFinalizers = finalizerExecutorFactory is not null
                    && snapshot.RunFinalizersOnMaxAttempts
                    && snapshot.Finalizers?.Count > 0;

                await SurfaceBestEffortDraftAsync(run.Id, result.Output.Markdown, result.StopReason,
                    setNote: !shouldRunFinalizers, cts.Token);

                if (shouldRunFinalizers)
                    await ExecuteFinalizationAsync(run.Id, snapshot, cts.Token);

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
                    ProviderName = cost.ProviderName,
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

    /// <summary>
    /// Runs the finalizer chain for a converged run. Reloads <c>FinalText</c> from the DB because
    /// <c>PostgresEventSink</c> writes it directly on <c>PipelineCompletedEvent</c>.
    /// Finalizer errors never abort the run — they are recorded as <see cref="ArtifactType.Status"/>
    /// artifacts and the error message is set on the run row (partial-success contract).
    /// </summary>
    private async Task ExecuteFinalizationAsync(
        Guid runId,
        CrewSnapshot snapshot,
        CancellationToken ct)
    {
        if (finalizerExecutorFactory is null) return;
        var finalizers = snapshot.Finalizers;
        if (finalizers is null || finalizers.Count == 0) return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtelierDbContext>();
        var artifactRepo = scope.ServiceProvider.GetRequiredService<IRunArtifactRepository>();

        var run = await db.Runs.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null) return;

        var finalText = run.FinalText ?? string.Empty;
        var completedAt = run.CompletedAt ?? DateTimeOffset.UtcNow;
        var currentText = finalText;
        var errorMessages = new List<string>();
        var costEntities = new List<FinalizationActorCost>();

        var baseContext = new FinalizerExecutionContext(
            RunId: runId,
            TemplateName: snapshot.TemplateName,
            FinalText: finalText,
            CurrentText: currentText,
            RunCompletedAt: completedAt);

        foreach (var profile in finalizers)
        {
            try
            {
                var executor = finalizerExecutorFactory.GetExecutor(profile.FinalizerType);
                var ctx = baseContext with { CurrentText = currentText };
                var result = await executor.ExecuteAsync(profile, ctx, ct);

                if (result.UpdatedText is not null)
                    currentText = result.UpdatedText;

                if (result.Artifact is not null)
                    await artifactRepo.CreateAsync(result.Artifact, ct);

                if (result.InputTokens > 0)
                {
                    costEntities.Add(new FinalizationActorCost
                    {
                        Id = Guid.NewGuid(),
                        RunId = runId,
                        ActorName = result.ActorName,
                        ModelName = result.ModelName,
                        InputTokens = result.InputTokens,
                        OutputTokens = result.OutputTokens,
                        CostEur = result.CostEur,
                        ProviderName = result.ProviderName,
                        CreatedAt = DateTimeOffset.UtcNow,
                    });
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Finalizer {Profile} threw unexpectedly for run {RunId}.",
                    profile.Name, runId);
                errorMessages.Add($"{profile.Name}: {ex.Message}");

                // Persist a Status artifact so the failure is visible in the Run detail UI.
                var errorArtifact = new RunArtifact
                {
                    Id = Guid.NewGuid(),
                    RunId = runId,
                    FinalizerProfileName = profile.Name,
                    ArtifactType = ArtifactType.Status,
                    StorageUri = "error",
                    StatusMessage = $"Unexpected error: {ex.Message}",
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                try { await artifactRepo.CreateAsync(errorArtifact, ct); }
                catch (Exception dbEx)
                {
                    logger.LogError(dbEx,
                        "Failed to persist error artifact for finalizer {Profile}, run {RunId}.",
                        profile.Name, runId);
                }
            }
        }

        // Persist finalizer costs
        if (costEntities.Count > 0)
        {
            db.FinalizationActorCosts.AddRange(costEntities);
            await db.SaveChangesAsync(ct);
        }

        // Update run: FinalText (if transforms changed it), FinalizerCostEur, FinalizerErrorMessage
        var totalFinalizerCost = costEntities.Any(c => c.CostEur.HasValue)
            ? costEntities.Sum(c => c.CostEur ?? 0m)
            : (decimal?)null;
        var errorMsg = errorMessages.Count > 0 ? string.Join("; ", errorMessages) : null;

        await db.Runs
            .Where(r => r.Id == runId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.FinalText,             currentText)
                .SetProperty(r => r.FinalizerCostEur,      totalFinalizerCost)
                .SetProperty(r => r.FinalizerErrorMessage,  errorMsg),
                ct);
    }

    /// <summary>
    /// Overrides the run's <c>FinalText</c> with the best-effort draft returned by the SDK
    /// (<see cref="GeefPipelineResult{TOutput}.Output"/> after a non-converging run).
    /// The run status has already been set to Completed by <c>PostgresEventSink</c> via the
    /// <c>PipelineCompletedEvent</c>; this method fixes up the text and optionally adds a
    /// convergence note.
    /// </summary>
    private async Task SurfaceBestEffortDraftAsync(
        Guid runId, string bestDraft, Geef.Sdk.Policies.ConvergenceDecision? stopReason,
        bool setNote, CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AtelierDbContext>();

            var note = setNote
                ? stopReason switch
                {
                    Geef.Sdk.Policies.ConvergenceDecision.StopMaxAttemptsReached =>
                        "Max iterations reached without full convergence. Best available draft returned.",
                    Geef.Sdk.Policies.ConvergenceDecision.StopTimeBudgetReached =>
                        "Time budget exceeded without full convergence. Best available draft returned.",
                    _ => "Pipeline stopped without full convergence. Best available draft returned."
                }
                : null;

            var updater = db.Runs.Where(r => r.Id == runId);
            await updater.ExecuteUpdateAsync(s => s
                .SetProperty(r => r.FinalText,  bestDraft)
                .SetProperty(r => r.WordCount,  Core.Domain.Dashboard.WordCounter.Count(bestDraft))
                .SetProperty(r => r.FinalizerErrorMessage, note),
                ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to surface best-effort draft for run {RunId}.", runId);
        }
    }
}
