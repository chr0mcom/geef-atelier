using System.Globalization;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;

namespace Geef.Atelier.Web.Components.UI.Orders;

/// <summary>Plain-English wording for orders and files, shared by the start page, the order page and "My work".</summary>
public static class OrderDisplayText
{
    /// <summary>Culture used for numbers and dates; the interface is English only.</summary>
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

    /// <summary>
    /// The state to show: an order with a text is never shown as failed (it is "Ready – please check" instead), even if
    /// the resolver should ever report that combination.
    /// </summary>
    public static OrderDisplayState ShownState(this OrderDisplay display) =>
        display is { State: OrderDisplayState.Failed, HasText: true } ? OrderDisplayState.DeliveredWithOpenPoints : display.State;

    /// <summary>The short label of a display state, e.g. "Ready – please check".</summary>
    public static string ToLabel(this OrderDisplayState state) => state switch
    {
        OrderDisplayState.Preparing => "Getting ready",
        OrderDisplayState.NeedsInput => "Needs your answers",
        OrderDisplayState.Queued => "Waiting to start",
        OrderDisplayState.Working => "Writing",
        OrderDisplayState.Delivered => "Ready",
        OrderDisplayState.DeliveredWithOpenPoints => "Ready – please check",
        OrderDisplayState.Failed => "No text – something went wrong",
        OrderDisplayState.Stopped => "Stopped",
        _ => state.ToString(),
    };

    /// <summary>The CSS tone of a display state: <c>busy</c>, <c>ready</c>, <c>check</c>, <c>failed</c> or <c>stopped</c>.</summary>
    public static string ToTone(this OrderDisplayState state) => state switch
    {
        OrderDisplayState.Delivered => "ready",
        OrderDisplayState.DeliveredWithOpenPoints => "check",
        OrderDisplayState.Failed => "failed",
        OrderDisplayState.Stopped => "stopped",
        OrderDisplayState.NeedsInput => "check",
        _ => "busy",
    };

    /// <summary>A stable lower-case name for test ids and data attributes, e.g. <c>delivered-with-open-points</c>.</summary>
    public static string ToKey(this OrderDisplayState state) => state switch
    {
        OrderDisplayState.Preparing => "preparing",
        OrderDisplayState.NeedsInput => "needs-input",
        OrderDisplayState.Queued => "queued",
        OrderDisplayState.Working => "working",
        OrderDisplayState.Delivered => "delivered",
        OrderDisplayState.DeliveredWithOpenPoints => "delivered-with-open-points",
        OrderDisplayState.Failed => "failed",
        OrderDisplayState.Stopped => "stopped",
        _ => state.ToString().ToLowerInvariant(),
    };

    /// <summary>True for states in which the order is still being worked on.</summary>
    public static bool IsBusy(this OrderDisplayState state) =>
        state is OrderDisplayState.Preparing or OrderDisplayState.NeedsInput or OrderDisplayState.Queued or OrderDisplayState.Working;

    /// <summary>A number with thousands separators, e.g. <c>160,485</c>.</summary>
    public static string Grouped(this int value) => value.ToString("N0", Culture);

    /// <summary>A file size such as <c>2.1 MB</c>.</summary>
    public static string ToFileSize(this long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => (bytes / 1024.0).ToString("0", Culture) + " KB",
        _ => (bytes / (1024.0 * 1024.0)).ToString("0.0", Culture) + " MB",
    };

    /// <summary>An elapsed time such as <c>2:05</c> or <c>1:02:05</c>.</summary>
    public static string ToClock(this TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        return elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
    }

    /// <summary>A date and time as shown in lists, e.g. <c>Nov 24, 2026, 14:05</c>.</summary>
    public static string ToListDate(this DateTimeOffset at) =>
        at.LocalDateTime.ToString("MMM d, yyyy, HH:mm", Culture);

    /// <summary>How much of a file is used, in plain words.</summary>
    public static string ToPlainText(this SourceBudgetClass budget) => budget switch
    {
        SourceBudgetClass.FullText => "Read completely",
        SourceBudgetClass.WithMap => "Read with a table of contents",
        SourceBudgetClass.TooLarge => "Too long to read completely",
        _ => budget.ToString(),
    };

    /// <summary>How a file went into the text, in plain words.</summary>
    public static string ToPlainText(this SourceUsageMode mode) => mode switch
    {
        SourceUsageMode.FullText => "read completely",
        SourceUsageMode.FullTextWithMap => "read with a table of contents",
        SourceUsageMode.Excluded => "not used",
        _ => mode.ToString(),
    };

    /// <summary>The summary line of a receipt, e.g. "80 pages · 160,485 characters · 2.1 MB · Read completely".</summary>
    public static string ToSummary(this SourceReceipt receipt)
    {
        var parts = new List<string>();
        if (receipt.PageCount is int pages)
            parts.Add(pages == 1 ? "1 page" : $"{pages.Grouped()} pages");
        parts.Add($"{receipt.CharCount.Grouped()} characters");
        parts.Add(receipt.SizeBytes.ToFileSize());
        parts.Add(receipt.BudgetClass.ToPlainText());
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// A warning about a file as one plain sentence. <paramref name="hasPages"/> is false for a file without pages
    /// (Word, text), where choosing pages is not possible.
    /// </summary>
    public static string ToPlainText(this SourceWarning warning, bool hasPages = true)
    {
        var pages = warning.Pages is { Count: > 0 } list
            ? $" ({(list.Count == 1 ? "page" : "pages")} {string.Join(", ", list.Take(12))}{(list.Count > 12 ? ", …" : "")})"
            : string.Empty;
        return warning.Code switch
        {
            SourceWarningCodes.PagesWithoutText => $"Some pages have no readable text{pages}. They may be pictures or scans.",
            SourceWarningCodes.LikelyScanned => "This looks like a scan. Only a little text could be read, so things may be missing.",
            SourceWarningCodes.Encrypted => "This file is protected with a password and cannot be read.",
            SourceWarningCodes.EncodingGuessed => "We had to guess the text encoding. Some special characters may look wrong.",
            SourceWarningCodes.ReplacementCharacters => "Some characters could not be read and were replaced.",
            SourceWarningCodes.LongLinesWrapped => "Very long lines were split so that they can be read.",
            SourceWarningCodes.DocxContentSkipped => "Pictures and similar parts of the Word file are left out. Only the text is used.",
            SourceWarningCodes.UsesDocumentMap => "This file is very long. The writer gets a table of contents and reads the parts that matter.",
            SourceWarningCodes.OverBudget => hasPages
                ? "This file is too long to use completely. Enter the pages to use below."
                : "This file is too long to use completely.",
            SourceWarningCodes.TooManyPages => "This file has too many pages.",
            SourceWarningCodes.Duplicate => "You added this file before. The copy you already have is used.",
            _ => string.IsNullOrWhiteSpace(warning.Message) ? "Something about this file needs your attention." : warning.Message + pages,
        };
    }
}
