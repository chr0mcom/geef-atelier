using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Checks;
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
        IReadOnlyList<RunWarningEntity>? warnings = null)
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
            warnings ?? []);
    }

    public static OrderResult Result(
        Guid orderId,
        OrderDisplayState state,
        string? text,
        IReadOnlyList<string>? placeholders = null,
        IReadOnlyList<DraftCheckResult>? openChecks = null,
        IReadOnlyList<OrderSourceUsageView>? sourcesUsed = null,
        IReadOnlyList<RunWarningEntity>? warnings = null,
        string? note = null) =>
        new(orderId,
            new OrderDisplay(state, !string.IsNullOrWhiteSpace(text), IsFinished(state)),
            Guid.NewGuid(), 1, text, text is null ? null : text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
            placeholders ?? [], openChecks ?? [], sourcesUsed ?? [], warnings ?? [], note);

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

    public Task<IReadOnlyList<OrderListItem>> ListAsync(string? ownerUsername, int limit = 50, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<OrderListItem>>([]);

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

    public Task<bool> CancelAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default) => Task.FromResult(false);

    public Task<bool> DeleteAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default) => Task.FromResult(false);

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
