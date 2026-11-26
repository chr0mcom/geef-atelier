using System.Collections.Concurrent;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Domain.Sources;
using Microsoft.AspNetCore.Components.Forms;

namespace Geef.Atelier.Tests.Web.Components.Orders;

/// <summary>In-memory <see cref="ISourceIntakeService"/>: receipts are set by the test, nothing is read or stored.</summary>
internal sealed class FakeSourceIntakeService : ISourceIntakeService
{
    private readonly ConcurrentDictionary<Guid, (string Owner, SourceReceipt Receipt)> _receipts = new();

    public SourceReceipt Add(string owner, SourceReceipt receipt)
    {
        _receipts[receipt.SourceId!.Value] = (owner, receipt);
        return receipt;
    }

    public Task<SourceReceipt> IntakeAsync(SourceUpload upload, string ownerUsername, SourceOrigin origin, CancellationToken ct = default, RefusalPath path = RefusalPath.Web) =>
        Task.FromResult(Add(ownerUsername, OrderReceipts.Usable(upload.FileName)));

    public Task<SourceReceipt> IntakeTextAsync(string title, string text, string ownerUsername, SourceOrigin origin, CancellationToken ct = default) =>
        Task.FromResult(Add(ownerUsername, OrderReceipts.Usable(title, chars: text.Length)));

    public Task<SourceReceipt?> GetReceiptAsync(Guid sourceId, string? requestingUsername, CancellationToken ct = default) =>
        Task.FromResult(_receipts.TryGetValue(sourceId, out var entry) && (requestingUsername is null || entry.Owner == requestingUsername)
            ? entry.Receipt
            : null);

    public Task<SourceDeleteResult> DeleteAsync(Guid sourceId, string? requestingUsername, CancellationToken ct = default) =>
        Task.FromResult(_receipts.TryRemove(sourceId, out _) ? SourceDeleteResult.Deleted : SourceDeleteResult.NotFound);
}

/// <summary>Synthetic receipts.</summary>
internal static class OrderReceipts
{
    public static SourceReceipt Usable(
        string fileName = "notes.md",
        int chars = 2_362,
        int? pages = null,
        long size = 4_096,
        SourceBudgetClass budget = SourceBudgetClass.FullText,
        IReadOnlyList<SourceWarning>? warnings = null,
        Guid? sourceId = null,
        bool duplicate = false) =>
        new(sourceId ?? Guid.NewGuid(), fileName, pages is null ? SourceKind.Markdown : SourceKind.Pdf, size, pages, chars, chars / 6,
            SourceQuality.Good, true, duplicate, budget, "The beginning of the file.", warnings ?? [], null);

    public static SourceReceipt Unusable(string fileName, string reason) =>
        new(null, fileName, null, 1_024, null, 0, 0, SourceQuality.Unreadable, false, false, SourceBudgetClass.FullText, string.Empty, [], reason);
}

/// <summary>Antiforgery state with a fixed token, as the server would hand it to the page.</summary>
internal sealed class FakeAntiforgeryStateProvider : AntiforgeryStateProvider
{
    public const string Token = "test-antiforgery-token";

    public override AntiforgeryRequestToken? GetAntiforgeryToken() => new(Token, "__RequestVerificationToken");
}
