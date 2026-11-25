using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Warnings;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Mcp.Orders;

internal sealed class FakeUser(string? username, bool isAdmin = false) : ICurrentUserService
{
    public string? Username => username;
    public bool IsAuthenticated => username is not null;
    public bool IsAdmin => isAdmin;
}

internal static class OrderToolTestData
{
    public static IOptions<OrderOptions> Options(int maxWaitSeconds = 50, OrderMode legacyMode = OrderMode.Direct) =>
        Microsoft.Extensions.Options.Options.Create(new OrderOptions { MaxWaitSeconds = maxWaitSeconds, LegacySubmitDefaultMode = legacyMode });

    public static IOptions<IntakeOptions> Intake(int submitWaitSeconds = 30) =>
        Microsoft.Extensions.Options.Options.Create(new IntakeOptions { SubmitWaitSeconds = submitWaitSeconds });

    public static readonly ClarifyQuestion LengthQuestion = new(
        "length", GapTopic.Length, "Wie lang soll der Text ungefähr werden?",
        [new QuestionOption("short", "Kurz (etwa 150 Wörter)"), new QuestionOption("medium", "Mittel (etwa 400 Wörter)"), new QuestionOption("long", "Lang (etwa 800 Wörter)")],
        null, true, true);

    public static readonly ClarifyQuestion AudienceQuestion = new(
        "q1", GapTopic.Audience, "Wer bekommt die Mail?",
        [new QuestionOption("o1", "Ein einzelner Kunde"), new QuestionOption("o2", "Alle Kunden")],
        "o1", false, false);

    public static readonly IntakeAssumption ToneAssumption = new("a1", GapTopic.Tone, "Freundlicher, sachlicher Ton", AssumptionConfidence.Sure);
    public static readonly IntakeAssumption DateAssumption = new("a2", GapTopic.Date, "Neuer Liefertermin als Platzhalter", AssumptionConfidence.Unsure);

    public static ClarificationView Clarification(IReadOnlyList<ClarifyQuestion>? questions = null, IntakeOutcome? outcome = IntakeOutcome.Asked) =>
        new(outcome, "email", "de", "Eine kurze Mail an einen Kunden über eine Lieferverzögerung.",
            [ToneAssumption, DateAssumption], questions ?? [LengthQuestion, AudienceQuestion], [], null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24));

    public static bool IsFinished(OrderDisplayState state) =>
        state is OrderDisplayState.Delivered or OrderDisplayState.DeliveredWithOpenPoints or OrderDisplayState.Failed or OrderDisplayState.Stopped;

    public static OrderView View(
        Guid orderId,
        OrderDisplayState state,
        string step = "Writing",
        TimeSpan? elapsed = null,
        Guid? runId = null,
        int? version = 1,
        RunOutcome? outcome = null,
        IReadOnlyList<RunWarningEntity>? warnings = null,
        ClarificationView? clarification = null)
    {
        var now = DateTimeOffset.UtcNow;
        var display = new OrderDisplay(state, HasText: state is OrderDisplayState.Delivered or OrderDisplayState.DeliveredWithOpenPoints, IsFinished(state));
        var run = runId is { } id
            ? new OrderRunView(id, version, RunKind.Standard, RunStatus.Running, outcome, now, now, null, null)
            : null;
        return new OrderView(
            orderId, "Synthetic order", "Write a short note.", OrderMode.Direct, OrderEntry.Mcp, OrderState.Ready, display,
            now, now, run, run is null ? [] : [run], [],
            new OrderProgress([], step, elapsed ?? TimeSpan.FromSeconds(12)),
            warnings ?? [],
            clarification);
    }

    public static OrderResult Result(
        Guid orderId,
        OrderDisplayState state,
        string? text,
        IReadOnlyList<string>? placeholders = null,
        IReadOnlyList<DraftCheckResult>? openChecks = null,
        IReadOnlyList<OrderSourceUsageView>? sourcesUsed = null,
        IReadOnlyList<RunWarningEntity>? warnings = null,
        string? note = null,
        IReadOnlyList<IntakeAssumption>? assumptions = null) =>
        new(orderId,
            new OrderDisplay(state, !string.IsNullOrWhiteSpace(text), IsFinished(state)),
            Guid.NewGuid(), 1, text, text is null ? null : text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
            placeholders ?? [], openChecks ?? [], sourcesUsed ?? [], warnings ?? [], note, assumptions);

    public static RunWarningEntity Warning(string code, string message) =>
        new() { Id = Guid.NewGuid(), RunId = Guid.NewGuid(), Source = "test", Code = code, Message = message, CreatedAt = DateTimeOffset.UtcNow };
}

internal sealed class FakeOrderService : IOrderService
{
    private readonly Dictionary<Guid, (string Owner, OrderView View)> _orders = [];
    private readonly Dictionary<Guid, OrderResult> _results = [];

    public List<SubmitOrderRequest> Submitted { get; } = [];
    public List<(Guid OrderId, string? RequestingUsername, OrderDisplayState? KnownState, TimeSpan MaxWait)> Waits { get; } = [];
    public List<(Guid OrderId, string? RequestingUsername)> Gets { get; } = [];
    public List<(Guid OrderId, string? RequestingUsername, string? ViewerUsername)> ResultReads { get; } = [];
    public List<AnswerOrderRequest> Answers { get; } = [];
    public List<(Guid OrderId, string? RequestingUsername)> Skips { get; } = [];
    public List<ReviseOrderRequest> Revisions { get; } = [];
    public List<(Guid OrderId, string? RequestingUsername)> Cancels { get; } = [];
    public List<(string? OwnerUsername, int Limit)> Lists { get; } = [];
    public List<(string Owner, OrderListItem Item)> ListItems { get; } = [];
    public Func<Guid, bool>? OnCancel { get; set; }
    public OrderView? ViewAfterCancel { get; set; }

    public Func<AnswerOrderRequest, OrderSubmission>? OnAnswer { get; set; }
    public Func<Guid, OrderSubmission>? OnSkip { get; set; }
    public Exception? AnswerException { get; set; }

    public Func<ReviseOrderRequest, OrderRevision>? OnRevise { get; set; }
    public Exception? ReviseException { get; set; }

    public Func<SubmitOrderRequest, OrderSubmission>? OnSubmit { get; set; }
    public Exception? SubmitException { get; set; }

    public OrderView? ViewAfterWait { get; set; }
    public bool DeleteDuringWait { get; set; }
    public bool ObservedCancellation { get; private set; }

    public void Add(string owner, OrderView view) => _orders[view.OrderId] = (owner, view);

    public void AddResult(string owner, OrderResult result)
    {
        _results[result.OrderId] = result;
        if (!_orders.ContainsKey(result.OrderId))
            _orders[result.OrderId] = (owner, OrderToolTestData.View(result.OrderId, result.Display.State));
    }

    public Task<OrderSubmission> SubmitAsync(SubmitOrderRequest request, CancellationToken ct = default)
    {
        Submitted.Add(request);
        if (SubmitException is not null)
            throw SubmitException;
        return Task.FromResult(OnSubmit?.Invoke(request)
            ?? new OrderSubmission(Guid.NewGuid(), OrderDisplayState.Queued, Guid.NewGuid(), []));
    }

    public Task<SourceSelectionCheck> CheckSourcesAsync(IReadOnlyList<OrderSourceSelection> sources, string ownerUsername, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<OrderView?> GetAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default)
    {
        Gets.Add((orderId, requestingUsername));
        return Task.FromResult(Visible(orderId, requestingUsername) ? _orders[orderId].View : null);
    }

    public Task<OrderResult?> GetResultAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default) =>
        Task.FromResult(Visible(orderId, requestingUsername) && _results.TryGetValue(orderId, out var result) ? result : null);

    public Task<OrderResult?> GetResultAsync(Guid orderId, string? requestingUsername, string? viewerUsername, CancellationToken ct = default)
    {
        ResultReads.Add((orderId, requestingUsername, viewerUsername));
        return GetResultAsync(orderId, requestingUsername, ct);
    }

    public Task<IReadOnlyList<OrderListItem>> ListAsync(string? ownerUsername, int limit = 50, CancellationToken ct = default)
    {
        Lists.Add((ownerUsername, limit));
        return Task.FromResult<IReadOnlyList<OrderListItem>>(
            ListItems.Where(i => ownerUsername is null || i.Owner == ownerUsername).Select(i => i.Item).Take(limit).ToList());
    }

    public async Task<OrderView?> WaitForChangeAsync(Guid orderId, string? requestingUsername, OrderDisplayState? knownState, TimeSpan maxWait, CancellationToken ct = default)
    {
        Waits.Add((orderId, requestingUsername, knownState, maxWait));
        if (DeleteDuringWait)
        {
            _orders.Remove(orderId);
            return null;
        }

        if (ViewAfterWait is not null)
            return ViewAfterWait;

        try
        {
            await Task.Delay(maxWait, ct);
        }
        catch (OperationCanceledException)
        {
            ObservedCancellation = true;
            throw;
        }

        return Visible(orderId, requestingUsername) ? _orders[orderId].View : null;
    }

    public Task<bool> CancelAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default)
    {
        Cancels.Add((orderId, requestingUsername));
        var changed = Visible(orderId, requestingUsername) && (OnCancel?.Invoke(orderId) ?? false);
        if (changed && ViewAfterCancel is not null)
            _orders[orderId] = (_orders[orderId].Owner, ViewAfterCancel);
        return Task.FromResult(changed);
    }

    public Task<bool> DeleteAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default) => Task.FromResult(false);

    public Task<OrderSubmission> AnswerAsync(AnswerOrderRequest request, CancellationToken ct = default)
    {
        Answers.Add(request);
        if (AnswerException is not null)
            throw AnswerException;
        return Task.FromResult(OnAnswer?.Invoke(request)
            ?? new OrderSubmission(request.OrderId, OrderDisplayState.Queued, Guid.NewGuid(), []));
    }

    public Task<OrderRevision> ReviseAsync(ReviseOrderRequest request, CancellationToken ct = default)
    {
        Revisions.Add(request);
        if (ReviseException is not null)
            throw ReviseException;
        return Task.FromResult(OnRevise?.Invoke(request) ?? new OrderRevision(request.OrderId, OrderDisplayState.Queued, Guid.NewGuid(), null, []));
    }

    public Task<OrderSubmission> SkipClarificationAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default)
    {
        Skips.Add((orderId, requestingUsername));
        if (AnswerException is not null)
            throw AnswerException;
        return Task.FromResult(OnSkip?.Invoke(orderId)
            ?? new OrderSubmission(orderId, OrderDisplayState.Queued, Guid.NewGuid(), []));
    }

    private bool Visible(Guid orderId, string? requestingUsername) =>
        _orders.TryGetValue(orderId, out var entry) && (requestingUsername is null || entry.Owner == requestingUsername);
}

internal sealed class FakeSourceIntake : ISourceIntakeService
{
    public sealed record Intake(string FileName, string? ContentType, long Length, byte[] Content, string Owner, SourceOrigin Origin);

    public List<Intake> Intakes { get; } = [];
    public List<(Guid SourceId, string? RequestingUsername)> Deletes { get; } = [];
    public Dictionary<string, string> UnusableReasons { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Duplicates { get; } = new(StringComparer.Ordinal);

    public async Task<SourceReceipt> IntakeAsync(SourceUpload upload, string ownerUsername, SourceOrigin origin, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await upload.Content.CopyToAsync(buffer, ct);
        var content = buffer.ToArray();
        Intakes.Add(new Intake(upload.FileName, upload.DeclaredContentType, upload.Length, content, ownerUsername, origin));

        if (UnusableReasons.TryGetValue(upload.FileName, out var reason))
            return new SourceReceipt(null, upload.FileName, null, content.Length, null, 0, 0, SourceQuality.Unreadable,
                Usable: false, Duplicate: false, SourceBudgetClass.FullText, string.Empty, [], reason);

        var duplicate = Duplicates.Contains(upload.FileName);
        IReadOnlyList<SourceWarning> warnings = upload.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? [new SourceWarning(SourceWarningCodes.PagesWithoutText, "Page 3 has no text.", [3])]
            : [];
        return new SourceReceipt(Guid.NewGuid(), upload.FileName,
            upload.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? SourceKind.Pdf : SourceKind.Text,
            content.Length, upload.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? 4 : null,
            content.Length, 3, upload.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? SourceQuality.Partial : SourceQuality.Good,
            Usable: true, Duplicate: duplicate, SourceBudgetClass.FullText, "preview", warnings, null);
    }

    public Task<SourceReceipt> IntakeTextAsync(string title, string text, string ownerUsername, SourceOrigin origin, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<SourceReceipt?> GetReceiptAsync(Guid sourceId, string? requestingUsername, CancellationToken ct = default) =>
        Task.FromResult<SourceReceipt?>(null);

    public Task<SourceDeleteResult> DeleteAsync(Guid sourceId, string? requestingUsername, CancellationToken ct = default)
    {
        Deletes.Add((sourceId, requestingUsername));
        return Task.FromResult(SourceDeleteResult.Deleted);
    }
}

internal static class Attachments
{
    public static readonly byte[] MinimalPdf =
        "%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n"u8.ToArray();

    public static readonly byte[] MinimalDocx = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x06, 0x00, 0x08, 0x00, 0x00, 0x00];

    public static string Json(params (string FileName, string ContentType, byte[] Content)[] files) =>
        System.Text.Json.JsonSerializer.Serialize(files.Select(f => new
        {
            filename = f.FileName,
            contentType = f.ContentType,
            contentBase64 = Convert.ToBase64String(f.Content),
        }));

    public static string Text(string fileName, string text, string contentType = "text/plain") =>
        Json((fileName, contentType, System.Text.Encoding.UTF8.GetBytes(text)));
}
