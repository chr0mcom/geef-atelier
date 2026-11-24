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
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Core.Notifications;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Infrastructure.Composition;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Infrastructure.Pipeline.Assembly;
using Geef.Atelier.Infrastructure.Pipeline.Direct;
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
    IRunWarningSink                     warningSink,
    IGroundingProviderFactory           groundingProviderFactory,
    IPricingCatalog?                    pricingCatalog = null,
    IOptions<CostTrackingOptions>?      costTrackingOptions = null,
    IFinalizerExecutorFactory?          finalizerExecutorFactory = null,
    IPipelineAssemblerSelector?         assemblerSelector = null) : BackgroundService
{
    internal const string ResultNotSavedMessage = "The result could not be saved completely.";
    internal const string UnexpectedErrorMessage = "The task stopped because of an unexpected error.";
    internal const string FinalizerUnexpectedErrorMessage = "The step stopped because of an unexpected error.";
    internal const string RestartedWhileFinishingMessage = "The result may be incomplete because the service was restarted while finishing.";

    private readonly OrchestratorOptions _opts = options.Value;
    private readonly IPipelineAssemblerSelector _assemblers = assemblerSelector ?? PipelineAssemblerSelector.Default;
    private readonly DateTimeOffset _createdAt = DateTimeOffset.UtcNow;
    private readonly SemaphoreSlim _slots = new(options.Value.MaxConcurrentRuns, options.Value.MaxConcurrentRuns);
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _runCts   = new();
    private readonly ConcurrentDictionary<Guid, Task>                    _runTasks  = new();

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RecoverCrashedRunsAsync(_createdAt, stoppingToken);
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
    /// Pending runs without a saved snapshot that were created before this service instance existed were still
    /// being prepared when the previous instance stopped; they can never become ready and are failed as well.
    /// Runs that were stopped while their finishing steps ran keep their terminal status but have no outcome; they
    /// get one afterwards (<see cref="RecoverRunsInterruptedWhileFinishingAsync"/>). That repair runs last and on its
    /// own: when it fails, it is logged and the resets before it stay in effect.
    /// </summary>
    private async Task RecoverCrashedRunsAsync(DateTimeOffset createdBefore, CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtelierDbContext>();

        var affected = await db.Runs
            .Where(r => r.Status == RunStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status,       RunStatus.Failed)
                .SetProperty(r => r.ErrorMessage, "Service restarted")
                .SetProperty(r => r.Outcome,      r => r.Outcome ?? RunOutcome.Failed)
                .SetProperty(r => r.CompletedAt,  DateTimeOffset.UtcNow),
                stoppingToken);

        if (affected > 0)
            logger.LogWarning("Crash recovery: marked {Count} stale Running runs as Failed.", affected);

        var unprepared = await FailUnpreparedRunsAsync(db, createdBefore, "Service restarted while the task was being prepared", stoppingToken);
        if (unprepared > 0)
            logger.LogWarning("Crash recovery: marked {Count} Pending runs without snapshot as Failed.", unprepared);

        try
        {
            var finishing = await RecoverRunsInterruptedWhileFinishingAsync(db, stoppingToken);
            if (finishing > 0)
                logger.LogWarning("Crash recovery: recorded an outcome for {Count} runs stopped while finishing.", finishing);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            logger.LogError("Crash recovery: runs stopped while finishing could not be given an outcome: {Error}", ex.ToRedactedLogText());
        }
    }

    /// <summary>
    /// Gives an outcome to runs whose process ended while their finishing steps ran: the status is already terminal
    /// (the event sink writes it when the pipeline ends), but the outcome is written only after the finishing steps,
    /// so such a run would count as "still working" forever. With a saved text the outcome is
    /// <see cref="RunOutcome.Converged"/> (status Completed) or <see cref="RunOutcome.DeliveredWithOpenPoints"/>,
    /// without one <see cref="RunOutcome.Failed"/>; each run gets the warning <see cref="RestartedWhileFinishingMessage"/>.
    /// Runs that finished before Step47 also have no outcome and must stay untouched. No column records when Step47
    /// went live, so the earliest completion time of a run that has an outcome serves as that point: every run with an
    /// outcome finished under Step47, and only runs that completed after it are considered. Until the first run has an
    /// outcome nothing is changed. A run that completed before that first outcome was written (only possible in the
    /// first minutes after the upgrade) is not repaired; that is the conservative side.
    /// </summary>
    private async Task<int> RecoverRunsInterruptedWhileFinishingAsync(AtelierDbContext db, CancellationToken ct)
    {
        var outcomesSince = await db.Runs
            .Where(r => r.Outcome != null && r.CompletedAt != null)
            .MinAsync(r => r.CompletedAt, ct);
        if (outcomesSince is null)
            return 0;

        var interrupted = await db.Runs
            .AsNoTracking()
            .Where(r => r.Outcome == null
                        && r.CompletedAt != null
                        && r.CompletedAt > outcomesSince
                        && (r.Status == RunStatus.Completed || r.Status == RunStatus.Failed || r.Status == RunStatus.Aborted))
            .Select(r => new { r.Id, r.Status, HasText = r.FinalText != null })
            .ToListAsync(ct);

        var recovered = 0;
        foreach (var run in interrupted)
        {
            var outcome = !run.HasText ? RunOutcome.Failed
                : run.Status == RunStatus.Completed ? RunOutcome.Converged
                : RunOutcome.DeliveredWithOpenPoints;
            var written = await db.Runs
                .Where(r => r.Id == run.Id && r.Outcome == null)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Outcome, outcome), ct);
            if (written == 0)
                continue;
            recovered++;
            await ReportWarningAsync(run.Id, RunWarningSources.Run, RunWarningCodes.RestartedWhileFinishing,
                RestartedWhileFinishingMessage);
        }

        return recovered;
    }

    private static Task<int> FailUnpreparedRunsAsync(AtelierDbContext db, DateTimeOffset createdBefore, string message, CancellationToken ct) =>
        db.Runs
            .Where(r => r.Status == RunStatus.Pending && r.CrewSnapshot == null && r.CreatedAt < createdBefore)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status,       RunStatus.Failed)
                .SetProperty(r => r.ErrorMessage, message)
                .SetProperty(r => r.Outcome,      RunOutcome.Failed)
                .SetProperty(r => r.CompletedAt,  DateTimeOffset.UtcNow),
                ct);

    /// <summary>
    /// Loads at most <c>_slots.CurrentCount</c> Pending runs from the database and dispatches each
    /// after an atomic claim. The orchestrator assumes a single instance: the crash recovery at start resets every
    /// Running run, so a second instance would fail the runs of the first.
    /// A Pending run without a snapshot is still being prepared (attachments are uploaded before the snapshot is
    /// written) and is left alone until it is ready or exceeds <see cref="OrchestratorOptions.PreparationTimeout"/>.
    /// </summary>
    private async Task PollAndDispatchAsync(CancellationToken stoppingToken)
    {
        var slotsAvailable = _slots.CurrentCount;
        if (slotsAvailable == 0)
            return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtelierDbContext>();

        var timedOut = await FailUnpreparedRunsAsync(
            db, DateTimeOffset.UtcNow - _opts.PreparationTimeout, "The task could not be prepared in time. Please submit it again.", stoppingToken);
        if (timedOut > 0)
            logger.LogWarning("Marked {Count} Pending runs without snapshot as Failed after the preparation timeout.", timedOut);

        var runs = await db.Runs
            .AsNoTracking()
            .Where(r => r.Status == RunStatus.Pending && r.CrewSnapshot != null)
            .OrderBy(r => r.CreatedAt)
            .Take(slotsAvailable)
            .ToListAsync(stoppingToken);

        foreach (var run in runs)
        {
            // Atomic claim: transitions Pending → Running; affectedRows=0 means another instance already picked it up.
            var claimed = await db.Runs
                .Where(r => r.Id == run.Id && r.Status == RunStatus.Pending && r.CrewSnapshot != null)
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
        using var warningScope = RunWarningScope.Begin(run.Id, warningSink);

        try
        {
            var sinkLogger = loggerFactory.CreateLogger($"PostgresEventSink[{run.Id}]");
            var sink       = new PostgresEventSink(run.Id, scopeFactory, runNotifier, sinkLogger);
            var snapshot   = CrewSnapshot.DeserializeForRun(run.CrewSnapshot);

            var costTrackingEnabled = costTrackingOptions?.Value.Enabled ?? false;
            var accumulator = costTrackingEnabled ? new RunCostAccumulator() : null;

            await using var scope = scopeFactory.CreateAsyncScope();
            var assemblyContext = new PipelineAssemblyContext(
                run, snapshot, scope.ServiceProvider, scopeFactory, llmClientResolver, convergenceOptions, loggerFactory,
                sink, accumulator, warningSink, groundingProviderFactory, pricingCatalog);
            var assembler = _assemblers.Select(run, snapshot);
            var runner = assembler.Assemble(assemblyContext);

            var result = await runner.RunAsync(run.BriefingText, cts.Token);

            await ReportFailedReviewersAsync(run.Id, snapshot, result.History);
            var delivered = result.ToDeliveredOutcome();

            if (result.Success)
            {
                if (accumulator is not null)
                    await FinalizeRunCostsAsync(run.Id, accumulator, cts.Token);
                await RunFinalizersSafelyAsync(run.Id, snapshot, cts.Token);
                await SetDeliveredOutcomeAsync(run.Id, delivered.Outcome, delivered.Iteration);
                await FinishAssemblyAsync(assembler, assemblyContext, result, cts.Token);
            }
            else
            {
                logger.LogWarning(
                    "Run {RunId} did not converge (stop: {Reason}, degraded iterations: {Degraded}). Surfacing best-effort draft.",
                    run.Id, result.StopReason, result.DegradedIterations);

                if (accumulator is not null)
                    await FinalizeRunCostsAsync(run.Id, accumulator, cts.Token);

                var shouldRunFinalizers = finalizerExecutorFactory is not null
                    && snapshot.RunFinalizersOnMaxAttempts
                    && snapshot.Finalizers?.Count > 0;

                if (await SurfaceBestEffortDraftAsync(run.Id, result.Output.Markdown, result.StopReason,
                        setNote: !shouldRunFinalizers, cts.Token))
                {
                    if (shouldRunFinalizers)
                        await RunFinalizersSafelyAsync(run.Id, snapshot, cts.Token);
                    else if (snapshot.Finalizers?.Count > 0)
                        await ReportWarningAsync(run.Id, RunWarningSources.Finalizer, RunWarningCodes.FinalizersNotRun,
                            "The finishing steps were not run because the text did not pass all checks.");
                    await SetDeliveredOutcomeAsync(run.Id, delivered.Outcome, delivered.Iteration);
                    await FinishAssemblyAsync(assembler, assemblyContext, result, cts.Token);
                }
                else
                {
                    await FailIncompleteResultAsync(run.Id);
                }
            }

            await TouchOrderSafelyAsync(run);
            await NotifySafelyAsync(run.Id);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Do not pass the already-cancelled stoppingToken — use None so the DB write completes.
            await OverrideToAbortedAsync(run.Id, "Service stopping");
            await TouchOrderSafelyAsync(run);
            await NotifySafelyAsync(run.Id);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // User-initiated cancellation: sink may have written Failed; override to Aborted.
            await OverrideToAbortedAsync(run.Id, "Cancelled by user");
            await TouchOrderSafelyAsync(run);
            await NotifySafelyAsync(run.Id);
        }
        catch (InvalidCrewSnapshotException ex)
        {
            logger.LogError("Run {RunId} has an unusable crew snapshot; failing it without a fallback crew: {Error}", run.Id, ex.ToRedactedLogText());
            await MarkRunFailedAsync(run.Id, ex.Message);
            await TouchOrderSafelyAsync(run);
            await NotifySafelyAsync(run.Id);
        }
        catch (Exception ex)
        {
            logger.LogError("Run {RunId} pipeline threw unhandled exception: {Error}", run.Id, ex.ToRedactedLogText());
            await MarkRunFailedAsync(run.Id, SanitizeErrorMessage(ex));
            await TouchOrderSafelyAsync(run);
            await NotifySafelyAsync(run.Id);
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

        await SetOutcomeAsync(runId, RunOutcome.Failed, null);
    }

    /// <summary>
    /// Produces the user-visible error message for a pipeline exception: always one of a few fixed plain-English
    /// sentences, never text taken from the exception (no keys, hosts, variable names, paths or stack traces); the
    /// details go to the log only. Walks the full inner-exception chain so that exceptions wrapped by the Geef SDK
    /// are handled by the most-specific matching rule.
    /// </summary>
    internal static string SanitizeErrorMessage(Exception ex)
    {
        for (var direct = ex; direct is not null; direct = direct.InnerException)
        {
            if (direct is DirectPathException)
                return direct.Message;
        }

        var current = ex;
        while (current is not null)
        {
            if (current is HttpRequestException { StatusCode: { } code })
                return (int)code switch
                {
                    400 => "The AI service rejected the request (code 400). Please check the model settings.",
                    401 => "The AI service rejected the sign-in (authentication failed).",
                    403 => "The AI service denied access.",
                    429 => "The AI service is busy right now (rate limit reached). Please try again later.",
                    >= 500 => "The AI service is unavailable right now. Please try again later.",
                    _ => $"The AI service answered with an error (code {(int)code})."
                };

            if (current is HttpRequestException)
                return "The AI service could not be reached (request failed). Please try again later.";

            if (current is TaskCanceledException)
                return "The AI service took too long to answer (timed out).";

            current = current.InnerException;
        }

        return UnexpectedErrorMessage;
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

        await SetOutcomeAsync(runId, RunOutcome.Aborted, null);
    }

    /// <summary>
    /// Records a failure outcome (<see cref="RunOutcome.Failed"/> or <see cref="RunOutcome.Aborted"/>). The first
    /// outcome wins: an outcome recorded earlier is not overwritten. Never throws.
    /// </summary>
    private async Task SetOutcomeAsync(Guid runId, RunOutcome outcome, int? deliveredIteration)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AtelierDbContext>();

            await db.Runs
                .Where(r => r.Id == runId && r.Outcome == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Outcome,            outcome)
                    .SetProperty(r => r.DeliveredIteration, deliveredIteration),
                    CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to record outcome {Outcome} for run {RunId}.", outcome, runId);
        }
    }

    /// <summary>
    /// Records the delivered outcome (<see cref="RunOutcome.Converged"/> or <see cref="RunOutcome.DeliveredWithOpenPoints"/>)
    /// once the text and the finishing steps are stored. The outcome is written only when the run has a terminal status
    /// and a saved final text; otherwise the run is failed with a warning instead of claiming a result it does not
    /// have. An outcome recorded earlier (for example by a cancellation) is kept. Never throws.
    /// </summary>
    private async Task SetDeliveredOutcomeAsync(Guid runId, RunOutcome outcome, int? deliveredIteration)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AtelierDbContext>();

            var written = await db.Runs
                .Where(r => r.Id == runId
                            && r.Outcome == null
                            && r.FinalText != null
                            && (r.Status == RunStatus.Completed || r.Status == RunStatus.Failed || r.Status == RunStatus.Aborted))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Outcome,            outcome)
                    .SetProperty(r => r.DeliveredIteration, deliveredIteration),
                    CancellationToken.None);
            if (written > 0)
                return;

            var alreadyRecorded = await db.Runs
                .Where(r => r.Id == runId)
                .Select(r => r.Outcome != null)
                .FirstOrDefaultAsync(CancellationToken.None);
            if (alreadyRecorded)
                return;
        }
        catch (Exception ex)
        {
            logger.LogError("Failed to record outcome {Outcome} for run {RunId}: {Error}", outcome, runId, ex.ToRedactedLogText());
            return;
        }

        logger.LogError("Run {RunId} finished, but its status or final text was not saved; recording it as failed.", runId);
        await FailIncompleteResultAsync(runId);
    }

    /// <summary>Marks a run whose result could not be stored completely as failed and tells the user why.</summary>
    private async Task FailIncompleteResultAsync(Guid runId)
    {
        await ReportWarningAsync(runId, RunWarningSources.Run, RunWarningCodes.ResultNotSaved, ResultNotSavedMessage);
        await MarkRunFailedAsync(runId, ResultNotSavedMessage);
    }

    /// <summary>
    /// Runs the finishing steps; a failure while storing their results never takes away the delivered text. It is
    /// logged and becomes a warning. Cancellation still propagates.
    /// </summary>
    private async Task RunFinalizersSafelyAsync(Guid runId, CrewSnapshot snapshot, CancellationToken ct)
    {
        try
        {
            await ExecuteFinalizationAsync(runId, snapshot, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError("Finishing steps of run {RunId} could not be completed: {Error}", runId, ex.ToRedactedLogText());
            await ReportWarningAsync(runId, RunWarningSources.Finalizer, RunWarningCodes.FinalizerFailed,
                "The finishing steps could not be completed.");
        }
    }

    /// <summary>
    /// Lets the assembler store results of its kind of run (the report of the direct path) after the outcome is
    /// recorded. A failure is logged and never changes the delivered result; cancellation propagates.
    /// </summary>
    private async Task FinishAssemblyAsync(
        IPipelineAssembler assembler, PipelineAssemblyContext context, GeefPipelineResult<FinalizedDocument> result, CancellationToken ct)
    {
        try
        {
            await assembler.OnRunFinishedAsync(context, result, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError("Results of run {RunId} could not be stored completely after it finished: {Error}", context.Run.Id, ex.ToRedactedLogText());
        }
    }

    /// <summary>Moves the order of a finished run to the top of "My work"; never throws.</summary>
    private async Task TouchOrderSafelyAsync(RunEntity run)
    {
        if (run.OrderId is not { } orderId)
            return;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var orders = scope.ServiceProvider.GetService<IOrderRepository>();
            if (orders is not null)
                await orders.TouchAsync(orderId, DateTimeOffset.UtcNow, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning("The order of run {RunId} could not be updated: {Error}", run.Id, ex.ToRedactedLogText());
        }
    }

    /// <summary>Tells live views that the run changed; a failing notifier never affects the run.</summary>
    private async Task NotifySafelyAsync(Guid runId)
    {
        try
        {
            await runNotifier.NotifyRunUpdatedAsync(runId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Live update for run {RunId} could not be sent.", runId);
        }
    }

    private Task ReportWarningAsync(Guid runId, string source, string code, string message, int? iteration = null) =>
        warningSink.ReportAsync(runId, source, code, message, iteration, CancellationToken.None);

    /// <summary>
    /// Records one warning per reviewer that could not complete its check in an iteration, naming the reviewer by the
    /// display name from the snapshot.
    /// </summary>
    private async Task ReportFailedReviewersAsync(Guid runId, CrewSnapshot snapshot, Geef.Sdk.Runtime.IterationHistory history)
    {
        foreach (var record in history.Records)
        {
            foreach (var review in record.EvaluationResult.FailedReviewers)
            {
                await ReportWarningAsync(runId, RunWarningSources.Review, RunWarningCodes.ReviewerFailed,
                    $"The check \"{snapshot.ReviewerDisplayName(review.ReviewerName)}\" could not be completed in round {record.Iteration} and was left out.",
                    record.Iteration);
            }
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
                    Id                = Guid.NewGuid(),
                    IterationId       = iter.Id,
                    ActorType         = cost.ActorType,
                    ActorName         = cost.ActorName,
                    ModelName         = cost.ModelName,
                    InputTokens       = cost.InputTokens,
                    OutputTokens      = cost.OutputTokens,
                    CachedInputTokens = cost.CachedInputTokens > 0 ? cost.CachedInputTokens : null,
                    ReasoningTokens   = cost.ReasoningTokens > 0 ? cost.ReasoningTokens : null,
                    CostEur           = cost.CostEur,
                    ProviderName      = cost.ProviderName,
                    CreatedAt         = DateTimeOffset.UtcNow
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

            // Token breakdown (Step43): run-level sums of cached-input / reasoning subsets.
            var cachedSum = costEntities.Sum(c => c.CachedInputTokens ?? 0);
            var reasoningSum = costEntities.Sum(c => c.ReasoningTokens ?? 0);

            await db.Runs
                .Where(r => r.Id == runId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.LlmCostEur,        llmCostEur)
                    .SetProperty(r => r.GroundingCostEur,  groundingCostNullable)
                    .SetProperty(r => r.TotalCostEur,      totalNullable)
                    .SetProperty(r => r.CachedInputTokens, cachedSum > 0 ? cachedSum : (int?)null)
                    .SetProperty(r => r.ReasoningTokens,   reasoningSum > 0 ? reasoningSum : (int?)null),
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
    /// A cancellation of <paramref name="ct"/> (user cancel or service stop) is not a step failure: it propagates
    /// without a warning or an error artifact, also when an executor swallowed it and returned an error result.
    /// The results of the steps that finished before the cancellation (changed text, costs, error notes) are stored
    /// first, so the run matches the artifacts those steps already wrote; the remaining steps do not run.
    /// </summary>
    private async Task ExecuteFinalizationAsync(
        Guid runId,
        CrewSnapshot snapshot,
        CancellationToken ct)
    {
        var finalizers = snapshot.Finalizers;
        if (finalizers is null || finalizers.Count == 0) return;
        if (finalizerExecutorFactory is null)
        {
            await ReportWarningAsync(runId, RunWarningSources.Finalizer, RunWarningCodes.FinalizersNotRun,
                "The finishing steps were not run because they are not available on this server.");
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtelierDbContext>();
        var artifactRepo = scope.ServiceProvider.GetRequiredService<IRunArtifactRepository>();

        var run = await db.Runs.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run?.FinalText is null) return;

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

        try
        {
            foreach (var profile in finalizers)
            {
                try
                {
                    var executor = finalizerExecutorFactory.GetExecutor(profile.FinalizerType);
                    var ctx = baseContext with { CurrentText = currentText };
                    var result = await executor.ExecuteAsync(profile, ctx, ct);
                    ct.ThrowIfCancellationRequested();

                    if (result.UpdatedText is not null)
                        currentText = result.UpdatedText;

                    if (result.Artifact is not null)
                        await artifactRepo.CreateAsync(result.Artifact.WithRedactedStatus(), ct);

                    await ReportFinalizerResultAsync(runId, profile, result);

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
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    logger.LogError("Finalizer {Profile} threw unexpectedly for run {RunId}: {Error}",
                        profile.Name, runId, ex.ToRedactedLogText());
                    var failedMessage = $"The finishing step \"{profile.DisplayNameOrName()}\" failed.";
                    errorMessages.Add(failedMessage);
                    await ReportWarningAsync(runId, RunWarningSources.Finalizer, RunWarningCodes.FinalizerFailed, failedMessage);

                    var errorArtifact = new RunArtifact
                    {
                        Id = Guid.NewGuid(),
                        RunId = runId,
                        FinalizerProfileName = profile.Name,
                        ArtifactType = ArtifactType.Status,
                        StorageUri = "error",
                        StatusMessage = FinalizerUnexpectedErrorMessage,
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
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (costEntities.Count > 0 || errorMessages.Count > 0 || !string.Equals(currentText, finalText, StringComparison.Ordinal))
            {
                try
                {
                    await StoreFinalizationAsync(db, runId, currentText, costEntities, errorMessages, CancellationToken.None);
                }
                catch (Exception storeEx)
                {
                    logger.LogError("Results of the finished steps of run {RunId} could not be stored after the cancellation: {Error}",
                        runId, storeEx.ToRedactedLogText());
                }
            }
            throw;
        }

        await StoreFinalizationAsync(db, runId, currentText, costEntities, errorMessages, ct);
    }

    private static async Task StoreFinalizationAsync(
        AtelierDbContext db,
        Guid runId,
        string currentText,
        List<FinalizationActorCost> costEntities,
        List<string> errorMessages,
        CancellationToken ct)
    {
        if (costEntities.Count > 0)
        {
            db.FinalizationActorCosts.AddRange(costEntities);
            await db.SaveChangesAsync(ct);
        }

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
    /// Turns silent finalizer outcomes into run warnings: a step that reported a skip reason, and status
    /// artifacts marked as error or warning.
    /// </summary>
    private async Task ReportFinalizerResultAsync(Guid runId, FinalizerProfile profile, FinalizerExecutionResult result)
    {
        var name = profile.DisplayNameOrName();
        if (result.SkipReason is { } skip)
            await ReportWarningAsync(runId, RunWarningSources.Finalizer, RunWarningCodes.FinalizerSkipped,
                $"The finishing step \"{name}\" was skipped because {skip.ToWarningDetail()}.");

        if (result.Artifact is { ArtifactType: ArtifactType.Status } artifact)
        {
            if (artifact.StorageUri is "error" or "warning")
                logger.LogWarning("Finalizer {Profile} reported {Kind} for run {RunId}: {Detail}",
                    profile.Name, artifact.StorageUri, runId, artifact.StatusMessage.RedactSecrets());
            if (artifact.StorageUri == "error")
                await ReportWarningAsync(runId, RunWarningSources.Finalizer, RunWarningCodes.FinalizerFailed,
                    $"The finishing step \"{name}\" failed.");
            else if (artifact.StorageUri == "warning")
                await ReportWarningAsync(runId, RunWarningSources.Finalizer, RunWarningCodes.FinalizerSkipped,
                    $"The finishing step \"{name}\" had no effect.");
        }
    }

    /// <summary>
    /// Overrides the run's <c>FinalText</c> with the best-effort draft returned by the SDK
    /// (<see cref="GeefPipelineResult{TOutput}.Output"/> after a non-converging run).
    /// The run status has already been set to Failed/Aborted by <c>PostgresEventSink</c> via the
    /// <c>PipelineFailedEvent</c> (the trailing <c>PipelineCompletedEvent</c> with
    /// <c>Success: false</c> leaves it untouched); this method replaces the last-iteration
    /// fallback text with the SDK-selected best draft and optionally adds a convergence note.
    /// Returns <c>false</c> when the draft could not be stored; the caller must then not report a delivered result.
    /// </summary>
    private async Task<bool> SurfaceBestEffortDraftAsync(
        Guid runId, string bestDraft, Geef.Sdk.Policies.ConvergenceDecision? stopReason,
        bool setNote, CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AtelierDbContext>();

            var note = setNote ? stopReason.ToBestVersionKeptMessage() : null;

            var updater = db.Runs.Where(r => r.Id == runId);
            var written = await updater.ExecuteUpdateAsync(s => s
                .SetProperty(r => r.FinalText,  bestDraft)
                .SetProperty(r => r.WordCount,  Core.Domain.Dashboard.WordCounter.Count(bestDraft))
                .SetProperty(r => r.FinalizerErrorMessage, note),
                ct);
            return written > 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError("Failed to surface best-effort draft for run {RunId}: {Error}", runId, ex.ToRedactedLogText());
            return false;
        }
    }
}
