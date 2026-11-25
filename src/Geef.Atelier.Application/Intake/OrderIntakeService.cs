using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Core.Notifications;
using Geef.Atelier.Core.Persistence.Intake;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Core.Persistence.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Application.Intake;

/// <summary>
/// The intake of one order (contract stage 2, 4.1): load, clarify, answer from the profile, gate, then start the first run
/// (no questions, or the clarifier failed) or store the questions. Every step that changes the order is atomic and checks the
/// state; when the person was faster ("Skip", "Stop") the result is dropped quietly. Never blocks an order: every failure
/// starts it with the request as written.
/// </summary>
internal sealed class OrderIntakeService(
    IOrderRepository orders,
    ISourceRepository sources,
    IIntakeShadowRepository shadows,
    IMemoryResolution memory,
    IClarifier clarifier,
    IOrderRunStarter starter,
    IOrderNotifier notifier,
    IOptions<IntakeOptions> options,
    ILogger<OrderIntakeService> logger,
    TimeProvider time,
    IntakeSweepDeferrals deferrals) : IOrderIntakeService
{
    /// <summary>Error code when sources or profile could not be loaded.</summary>
    public const string PrepareFailedCode = "prepare_failed";

    /// <summary>Error code of an order that waited too long for a claim.</summary>
    public const string QueueTimeoutCode = "queue_timeout";

    /// <summary>Error code of an order whose claims are used up.</summary>
    public const string AttemptsExhaustedCode = "attempts_exhausted";

    /// <summary>Extra time on top of <see cref="IntakeOptions.Timeout"/> before the service stops waiting for the clarifier itself.</summary>
    public static readonly TimeSpan TimeoutGrace = TimeSpan.FromSeconds(5);

    /// <summary>Most orders handled per sweep.</summary>
    public const int SweepBatch = 20;

    private sealed record Analysis(IntakeRecord Record, int QuestionCount, bool Ok);

    public async Task<IntakeProcessResult> ProcessAsync(Guid orderId, CancellationToken ct)
    {
        var order = await orders.GetAsync(orderId, ct);
        if (order is null)
            return IntakeProcessResult.NotFound;
        if (order.State != OrderState.Analyzing)
            return IntakeProcessResult.NotAnalyzing;

        var analysis = await AnalyseAsync(order, ct);
        if (!analysis.Ok)
        {
            var started = await starter.TryStartAsync(new RunStartRequest(order, OrderState.Analyzing, analysis.Record, [], IntakeOutcome.Failed, true), ct);
            return Lost(orderId, started is not null, IntakeProcessResult.StartedAfterFailure);
        }

        if (analysis.QuestionCount == 0)
        {
            var started = await starter.TryStartAsync(new RunStartRequest(order, OrderState.Analyzing, analysis.Record, [], IntakeOutcome.Clarified, true), ct);
            return Lost(orderId, started is not null, IntakeProcessResult.Started);
        }

        var saved = await orders.TrySaveQuestionsAsync(orderId, IntakeRecordJson.Serialize(analysis.Record), time.GetUtcNow(), ct);
        if (saved)
            await NotifyAsync(orderId, ct);
        return Lost(orderId, saved, IntakeProcessResult.AskedQuestions);
    }

    public async Task<IntakeProcessResult> ProcessShadowAsync(Guid orderId, CancellationToken ct)
    {
        var settings = options.Value;
        var now = time.GetUtcNow();
        var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        if (await shadows.CountCompletedSinceAsync(dayStart, ct) >= settings.ShadowDailyCap)
        {
            await shadows.CompleteAsync(orderId, IntakeShadowStatuses.SkippedCap, null, null, null, now, ct);
            return IntakeProcessResult.ShadowSkipped;
        }

        var order = await orders.GetAsync(orderId, ct);
        if (order is null)
            return IntakeProcessResult.NotFound;

        var analysis = await AnalyseAsync(order, ct);
        var status = analysis.Record.Status switch
        {
            IntakeRecordStatus.Ok => IntakeShadowStatuses.Done,
            IntakeRecordStatus.TimedOut => IntakeShadowStatuses.TimedOut,
            _ => IntakeShadowStatuses.Failed,
        };
        await shadows.CompleteAsync(orderId, status, analysis.Record.DurationMs, analysis.Ok ? analysis.QuestionCount : null,
            IntakeRecordJson.Serialize(analysis.Record), time.GetUtcNow(), ct);
        return IntakeProcessResult.ShadowRecorded;
    }

    public async Task<int> StartStuckAsync(DateTimeOffset now, CancellationToken ct)
    {
        var settings = options.Value;
        var deferred = deferrals.Active();
        var ids = await orders.ListStuckIntakesAsync(now - settings.ClaimLease, now - settings.QueueTimeout, settings.MaxAttempts, SweepBatch + deferred.Count, ct);
        return await SweepEachAsync(ids, deferred, async id =>
        {
            var order = await orders.GetAsync(id, ct);
            if (order is not { State: OrderState.Analyzing })
                return false;
            var code = order.IntakeAttempts >= settings.MaxAttempts ? AttemptsExhaustedCode : QueueTimeoutCode;
            var record = IntakeRecord.Fallback(IntakeRecordStatus.QueueTimeout, code, 0, null, settings.BriefMode);
            if (await starter.TryStartAsync(new RunStartRequest(order, OrderState.Analyzing, record, [], IntakeOutcome.Failed, true), ct) is null)
                return false;
            logger.LogWarning("Intake of order {OrderId} did not finish ({Code}); it started with the request as written.", id, code);
            return true;
        }, ct);
    }

    public async Task<int> AutoStartUnansweredAsync(DateTimeOffset now, CancellationToken ct)
    {
        var after = options.Value.NeedsInputAutoStartAfter;
        if (after <= TimeSpan.Zero)
            return 0;

        var deferred = deferrals.Active();
        var ids = await orders.ListUnansweredAsync(now - after, SweepBatch + deferred.Count, ct);
        return await SweepEachAsync(ids, deferred, async id =>
        {
            var order = await orders.GetAsync(id, ct);
            if (order is not { State: OrderState.NeedsInput })
                return false;
            var record = IntakeRecordJson.TryParse(order.ClarifyJson);
            if (await starter.TryStartAsync(new RunStartRequest(order, OrderState.NeedsInput, record, [], IntakeOutcome.AutoStarted, false), ct) is null)
                return false;
            logger.LogInformation("Order {OrderId} got no answers in time and started with the assumptions.", id);
            return true;
        }, ct);
    }

    private async Task<int> SweepEachAsync(IReadOnlyList<Guid> ids, IReadOnlySet<Guid> deferred, Func<Guid, Task<bool>> start, CancellationToken ct)
    {
        var count = 0;
        foreach (var id in ids.Where(i => !deferred.Contains(i)).Take(SweepBatch))
        {
            try
            {
                if (await start(id))
                    count++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                var pause = deferrals.Defer(id);
                logger.LogError("Intake sweep: order {OrderId} could not be started and is left out for {Minutes} minutes: {Error}",
                    id, (int)pause.TotalMinutes, ex.ToRedactedLogText());
            }
        }
        return count;
    }

    private async Task<Analysis> AnalyseAsync(OrderEntity order, CancellationToken ct)
    {
        var settings = options.Value;
        ClarifierRequest request;
        MemoryFacts facts;
        IReadOnlyList<string> materialTexts;
        try
        {
            var items = await sources.ListForOrderAsync(order.Id, includeText: true, ct);
            facts = await memory.LoadAsync(order.CreatedByUser ?? string.Empty, ct);
            request = IntakeAnalysis.Request(order.RequestText, items, facts.Profile, settings);
            materialTexts = items.MaterialTexts();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError("Intake of order {OrderId}: files or profile could not be loaded: {Error}", order.Id, ex.ToRedactedLogText());
            return new Analysis(IntakeRecord.Fallback(IntakeRecordStatus.Failed, PrepareFailedCode, 0, null, settings.BriefMode), 0, false);
        }

        var call = await CallAsync(request, settings, ct);
        if (call.Status != IntakeRecordStatus.Ok || call.Output is not { } output)
        {
            var status = call.Status == IntakeRecordStatus.Ok ? IntakeRecordStatus.Invalid : call.Status;
            return new Analysis(IntakeRecord.Fallback(status, call.ErrorCode, call.DurationMs, call.Model, settings.BriefMode), 0, false);
        }

        var analysis = IntakeAnalysis.Analyse(
            order.RequestText, request.Candidates(), output, memory.Resolve(facts, output.TextType), settings.MaxQuestions,
            materialTexts, settings.DropGuessedDateOptions);
        var gate = analysis.Gate;
        var revision = analysis.Revision;
        var record = new IntakeRecord(
            IntakeRecord.CurrentSchemaVersion,
            IntakeRecordStatus.Ok,
            call.Model,
            call.DurationMs,
            null,
            analysis.Output,
            gate.Trace,
            gate.Questions,
            gate.Assumptions,
            gate.FromMemory,
            gate.ResearchTopics,
            order.Mode == OrderMode.Direct ? revision?.SourceId : null,
            settings.BriefMode);
        return new Analysis(record, gate.Questions.Count, true);
    }

    private async Task<ClarifierCallResult> CallAsync(ClarifierRequest request, IntakeOptions settings, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(settings.Timeout + TimeoutGrace);
        try
        {
            return await clarifier.ClarifyAsync(request, limit.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ClarifierCallResult(IntakeRecordStatus.TimedOut, null, null, Elapsed(started), "timeout");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError("Clarifier threw for an order: {Error}", ex.ToRedactedLogText());
            return new ClarifierCallResult(IntakeRecordStatus.Failed, null, null, Elapsed(started), "error");
        }
    }

    private int Elapsed(long started) => (int)Math.Min(int.MaxValue, time.GetElapsedTime(started).TotalMilliseconds);

    private IntakeProcessResult Lost(Guid orderId, bool applied, IntakeProcessResult result)
    {
        if (applied)
            return result;
        logger.LogInformation("Intake of order {OrderId}: the order changed meanwhile ({Result} dropped).", orderId, result);
        return IntakeProcessResult.NotAnalyzing;
    }

    private async Task NotifyAsync(Guid orderId, CancellationToken ct)
    {
        try
        {
            await notifier.NotifyOrderUpdatedAsync(orderId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Intake of order {OrderId}: live update failed: {Error}", orderId, ex.ToRedactedLogText());
        }
    }
}
