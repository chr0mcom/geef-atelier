using System.Collections.Concurrent;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Warnings;

namespace Geef.Atelier.Tests.Web.Components.Orders;

/// <summary>
/// In-memory <see cref="IOrderService"/> for page tests: orders are views kept in memory, results are set by the test,
/// every call is recorded. Ownership works like the real service: <c>requestingUsername = null</c> sees everything.
/// </summary>
internal sealed class FakeOrderService : IOrderService
{
    private readonly ConcurrentDictionary<Guid, (string Owner, OrderView View)> _orders = new();
    private readonly ConcurrentDictionary<Guid, OrderResult> _results = new();
    private int _calls;
    private int _active;
    private int _maxActive;

    public ConcurrentQueue<SubmitOrderRequest> Submitted { get; } = new();
    public ConcurrentQueue<Guid> Cancelled { get; } = new();
    public ConcurrentQueue<Guid> Deleted { get; } = new();
    public ConcurrentQueue<string?> CancelledFor { get; } = new();
    public ConcurrentQueue<string?> DeletedFor { get; } = new();
    public ConcurrentQueue<string?> ListedFor { get; } = new();
    public ConcurrentQueue<IReadOnlyList<OrderSourceSelection>> Checked { get; } = new();
    public ConcurrentQueue<AnswerOrderRequest> Answered { get; } = new();
    public ConcurrentQueue<(Guid OrderId, string? RequestingUsername)> Skipped { get; } = new();
    public ConcurrentQueue<ReviseOrderRequest> Revised { get; } = new();
    public Exception? ReviseError { get; set; }
    public Func<ReviseOrderRequest, OrderRevision>? OnRevise { get; set; }
    public Exception? AnswerError { get; set; }
    public Exception? SkipError { get; set; }
    public IReadOnlyList<string> StartNotices { get; set; } = [];
    public Func<IReadOnlyList<OrderSourceSelection>, SourceSelectionCheck> CheckSources { get; set; } = _ => new SourceSelectionCheck(0, SourceBudgetClass.FullText, null);
    public Exception? SubmitError { get; set; }
    public Exception? GetError { get; set; }
    public bool CancelResult { get; set; } = true;
    public bool DeleteResult { get; set; } = true;
    public Exception? DeleteError { get; set; }
    public TaskCompletionSource? Hold { get; set; }
    public int Calls => Volatile.Read(ref _calls);
    public int MaxActive => Volatile.Read(ref _maxActive);

    public OrderView Add(string owner, OrderView view)
    {
        _orders[view.OrderId] = (owner, view);
        return view;
    }

    public void SetResult(OrderResult result) => _results[result.OrderId] = result;

    public void Update(Guid orderId, Func<OrderView, OrderView> change)
    {
        var (owner, view) = _orders[orderId];
        _orders[orderId] = (owner, change(view));
    }

    public async Task<OrderSubmission> SubmitAsync(SubmitOrderRequest request, CancellationToken ct = default)
    {
        await EnterAsync();
        try
        {
            Submitted.Enqueue(request);
            if (SubmitError is not null)
                throw SubmitError;
            var view = OrderTestData.View(OrderDisplayState.Queued, title: Title(request.RequestText), requestText: request.RequestText, mode: request.Mode);
            Add(request.CreatedByUser, view);
            return new OrderSubmission(view.OrderId, OrderDisplayState.Queued, view.CurrentRun?.RunId, []);
        }
        finally
        {
            Leave();
        }
    }

    public async Task<SourceSelectionCheck> CheckSourcesAsync(IReadOnlyList<OrderSourceSelection> sources, string ownerUsername, CancellationToken ct = default)
    {
        await EnterAsync();
        try
        {
            Checked.Enqueue(sources);
            return CheckSources(sources);
        }
        finally
        {
            Leave();
        }
    }

    public async Task<OrderView?> GetAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default)
    {
        await EnterAsync();
        try
        {
            if (GetError is not null)
                throw GetError;
            return _orders.TryGetValue(orderId, out var entry) && (requestingUsername is null || entry.Owner == requestingUsername)
                ? entry.View
                : null;
        }
        finally
        {
            Leave();
        }
    }

    public async Task<OrderResult?> GetResultAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default)
    {
        await EnterAsync();
        try
        {
            return _orders.TryGetValue(orderId, out var entry) && (requestingUsername is null || entry.Owner == requestingUsername)
                   && _results.TryGetValue(orderId, out var result)
                ? result
                : null;
        }
        finally
        {
            Leave();
        }
    }

    public async Task<IReadOnlyList<OrderListItem>> ListAsync(string? ownerUsername, int limit = 50, CancellationToken ct = default)
    {
        await EnterAsync();
        try
        {
            ListedFor.Enqueue(ownerUsername);
            return _orders.Values
                .Where(e => ownerUsername is null || e.Owner == ownerUsername)
                .Select(e => e.View)
                .OrderByDescending(v => v.UpdatedAt)
                .Take(limit)
                .Select(v => new OrderListItem(v.OrderId, v.Title, v.Display, v.Mode, v.CreatedAt, v.UpdatedAt, v.Sources.Count, v.CurrentRun?.WordCount))
                .ToList();
        }
        finally
        {
            Leave();
        }
    }

    public Task<OrderView?> WaitForChangeAsync(Guid orderId, string? requestingUsername, OrderDisplayState? knownState, TimeSpan maxWait, CancellationToken ct = default) =>
        GetAsync(orderId, requestingUsername, ct);

    public async Task<bool> CancelAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default)
    {
        await EnterAsync();
        try
        {
            Cancelled.Enqueue(orderId);
            CancelledFor.Enqueue(requestingUsername);
            if (!Owns(orderId, requestingUsername))
                return false;
            if (CancelResult)
                Update(orderId, v => v with { Display = new OrderDisplay(OrderDisplayState.Stopped, v.Display.HasText, true) });
            return CancelResult;
        }
        finally
        {
            Leave();
        }
    }

    public async Task<bool> DeleteAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default)
    {
        await EnterAsync();
        try
        {
            Deleted.Enqueue(orderId);
            DeletedFor.Enqueue(requestingUsername);
            if (DeleteError is not null)
                throw DeleteError;
            return DeleteResult && Owns(orderId, requestingUsername) && _orders.TryRemove(orderId, out _);
        }
        finally
        {
            Leave();
        }
    }

    public async Task<OrderSubmission> AnswerAsync(AnswerOrderRequest request, CancellationToken ct = default)
    {
        await EnterAsync();
        try
        {
            Answered.Enqueue(request);
            if (AnswerError is not null)
                throw AnswerError;
            return Start(request.OrderId, request.RequestingUsername, IntakeOutcome.Answered, request.Answers);
        }
        finally
        {
            Leave();
        }
    }

    public Task<OrderRevision> ReviseAsync(ReviseOrderRequest request, CancellationToken ct = default)
    {
        Revised.Enqueue(request);
        if (ReviseError is not null)
            throw ReviseError;
        return Task.FromResult(OnRevise?.Invoke(request) ?? new OrderRevision(request.OrderId, OrderDisplayState.Queued, Guid.NewGuid(), null, []));
    }

    public async Task<OrderSubmission> SkipClarificationAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default)
    {
        await EnterAsync();
        try
        {
            Skipped.Enqueue((orderId, requestingUsername));
            if (SkipError is not null)
                throw SkipError;
            return Start(orderId, requestingUsername, IntakeOutcome.Skipped, []);
        }
        finally
        {
            Leave();
        }
    }

    private OrderSubmission Start(Guid orderId, string? requestingUsername, IntakeOutcome outcome, IReadOnlyList<OrderAnswer> answers)
    {
        if (!Owns(orderId, requestingUsername))
            throw new InvalidOperationException("Unknown order.");
        var (_, current) = _orders[orderId];
        if (current.Display.State is not (OrderDisplayState.Preparing or OrderDisplayState.NeedsInput))
            return new OrderSubmission(orderId, current.Display.State, current.CurrentRun?.RunId, ["This order has already started."], current.Clarification);
        var queued = OrderTestData.View(OrderDisplayState.Queued, orderId: orderId, title: current.Title, requestText: current.RequestText, mode: current.Mode)
            with { Clarification = current.Clarification is { } c ? c with { Outcome = outcome, Answers = answers } : null };
        Update(orderId, _ => queued);
        return new OrderSubmission(orderId, OrderDisplayState.Queued, queued.CurrentRun?.RunId, StartNotices, queued.Clarification);
    }

    private async Task EnterAsync()
    {
        Interlocked.Increment(ref _calls);
        var active = Interlocked.Increment(ref _active);
        int seen;
        while (active > (seen = Volatile.Read(ref _maxActive)) && Interlocked.CompareExchange(ref _maxActive, active, seen) != seen)
        {
        }
        if (Hold is { } hold)
            await hold.Task;
    }

    private void Leave() => Interlocked.Decrement(ref _active);

    private bool Owns(Guid orderId, string? requestingUsername) =>
        _orders.TryGetValue(orderId, out var entry) && (requestingUsername is null || entry.Owner == requestingUsername);

    private static string Title(string text) => text.Length > 60 ? text[..60] : text;
}

/// <summary>Builders for synthetic order views and results.</summary>
internal static class OrderTestData
{
    public static readonly DateTimeOffset Start = new(2026, 11, 24, 10, 0, 0, TimeSpan.Zero);

    public static bool IsFinished(OrderDisplayState state) =>
        state is OrderDisplayState.Delivered or OrderDisplayState.DeliveredWithOpenPoints or OrderDisplayState.Failed or OrderDisplayState.Stopped;

    public static OrderView View(
        OrderDisplayState state,
        bool? hasText = null,
        string title = "A short email about a delivery delay",
        string requestText = "Write a short, friendly email to a customer about a two-week delivery delay.",
        OrderMode mode = OrderMode.Direct,
        Guid? orderId = null,
        IReadOnlyList<OrderSourceView>? sources = null,
        IReadOnlyList<RunWarningEntity>? warnings = null,
        DateTimeOffset? updatedAt = null,
        int? wordCount = null)
    {
        var text = hasText ?? state is OrderDisplayState.Delivered or OrderDisplayState.DeliveredWithOpenPoints;
        var id = orderId ?? Guid.NewGuid();
        var runId = Guid.NewGuid();
        var (status, outcome) = state switch
        {
            OrderDisplayState.Queued => (RunStatus.Pending, (RunOutcome?)null),
            OrderDisplayState.Working => (RunStatus.Running, null),
            OrderDisplayState.Delivered => (RunStatus.Completed, RunOutcome.Converged),
            OrderDisplayState.DeliveredWithOpenPoints => (RunStatus.Failed, RunOutcome.DeliveredWithOpenPoints),
            OrderDisplayState.Failed => (RunStatus.Failed, RunOutcome.Failed),
            OrderDisplayState.Stopped => (RunStatus.Aborted, RunOutcome.Aborted),
            _ => (RunStatus.Pending, null),
        };
        var run = state is OrderDisplayState.Preparing or OrderDisplayState.NeedsInput
            ? null
            : new OrderRunView(runId, 1, RunKind.Standard, status, outcome, Start, Start.AddSeconds(5), IsFinished(state) ? Start.AddMinutes(3) : null, wordCount ?? (text ? 120 : null));
        return new OrderView(
            id,
            title,
            requestText,
            mode,
            OrderEntry.Web,
            OrderState.Ready,
            new OrderDisplay(state, text, IsFinished(state)),
            Start,
            updatedAt ?? Start.AddMinutes(3),
            run,
            run is null ? [] : [run],
            sources ?? [],
            Progress(state),
            warnings ?? []);
    }

    public static OrderProgress Progress(OrderDisplayState state)
    {
        var finished = IsFinished(state);
        var writing = state == OrderDisplayState.Working;
        return new OrderProgress(
            [
                new OrderStep("write", "Writing", finished ? OrderStepStatus.Done : writing ? OrderStepStatus.Active : OrderStepStatus.Pending, writing || finished ? Start.AddSeconds(5) : null, finished ? Start.AddMinutes(2) : null),
                new OrderStep("check", "Checking", finished ? OrderStepStatus.Done : OrderStepStatus.Pending, finished ? Start.AddMinutes(2) : null, finished ? Start.AddMinutes(3) : null),
                new OrderStep("done", "Done", finished ? OrderStepStatus.Done : OrderStepStatus.Pending, null, null),
            ],
            writing ? "Writing" : finished ? "Done" : "Waiting to start",
            TimeSpan.FromSeconds(75));
    }

    public static OrderResult Result(
        OrderView view,
        string text = "Dear Ms Example,\n\nyour order will arrive two weeks later than planned. [delivery date]",
        IReadOnlyList<string>? placeholders = null,
        IReadOnlyList<DraftCheckResult>? openChecks = null,
        IReadOnlyList<OrderSourceUsageView>? sourcesUsed = null,
        string? note = null) =>
        new(view.OrderId, view.Display, view.CurrentRun?.RunId, view.CurrentRun?.Version, text, 17,
            placeholders ?? [], openChecks ?? [], sourcesUsed ?? [], view.Warnings, note);

    public static OrderView Analyzing(Guid? orderId = null, string title = "Invitation to the summer party") =>
        View(OrderDisplayState.Preparing, orderId: orderId, title: title, requestText: "Schreib eine Einladung zum Sommerfest.") with { State = OrderState.Analyzing };

    public static OrderView Asking(ClarificationView clarification, Guid? orderId = null, string title = "Invitation to the summer party") =>
        View(OrderDisplayState.NeedsInput, orderId: orderId, title: title, requestText: "Schreib eine Einladung zum Sommerfest.") with
        {
            State = OrderState.NeedsInput,
            Clarification = clarification,
        };

    public static ClarifyQuestion LengthQuestion() =>
        new("length", GapTopic.Length, "Wie lang soll der Text ungefähr werden?",
            [new("short", "Kurz (etwa 150 Wörter)"), new("medium", "Mittel (etwa 400 Wörter)"), new("long", "Lang (etwa 800 Wörter)")],
            null, true, true);

    public static ClarifyQuestion Question(int number, GapTopic slot = GapTopic.Date, params string[] options) =>
        new($"q{number}", slot, $"Frage {number}: Wann findet das Fest statt?",
            (options.Length == 0 ? ["Am Freitag", "Am Samstag"] : options).Select((label, i) => new QuestionOption($"o{i + 1}", label)).ToList(),
            null, true, false);

    public static ClarificationView Clarification(
        IReadOnlyList<ClarifyQuestion>? questions = null,
        IReadOnlyList<IntakeAssumption>? assumptions = null,
        IReadOnlyList<OrderAnswer>? answers = null,
        IntakeOutcome? outcome = IntakeOutcome.Asked,
        string? revisionFileName = null,
        DateTimeOffset? autoStartAt = null,
        string language = "de") =>
        new(outcome, "Einladung", language, "Eine Einladung zum Sommerfest des Teams, locker und herzlich.",
            assumptions ?? [new IntakeAssumption("a1", GapTopic.Tone, "Lockerer, herzlicher Ton", AssumptionConfidence.Unsure),
                            new IntakeAssumption("a2", GapTopic.Date, "Datum und Ort als Platzhalter", AssumptionConfidence.Sure)],
            questions ?? [LengthQuestion()],
            answers ?? [],
            revisionFileName,
            Start,
            autoStartAt);

    public static OrderSourceView Source(string fileName = "notes.md", int chars = 2_362, int? pages = null) =>
        new(Guid.NewGuid(), fileName, pages is null ? SourceKind.Markdown : SourceKind.Pdf, pages, chars, SourceQuality.Good, null, []);
}
