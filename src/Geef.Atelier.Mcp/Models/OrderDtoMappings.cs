using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Domain.Orders;

namespace Geef.Atelier.Mcp.Models;

/// <summary>Maps the order service results to the MCP answers.</summary>
internal static class OrderDtoMappings
{
    public const int NextCheckWhileWorkingSeconds = 10;

    public static OrderSourceDto ToDto(this SourceReceipt receipt) =>
        new(receipt.FileName, receipt.PageCount, receipt.CharCount, receipt.Quality.ToWire(),
            receipt.Warnings.Select(w => w.Message).ToList());

    public static OrderSubmittedDto ToDto(this OrderSubmission submission, IReadOnlyList<SourceReceipt> receipts) =>
        new(submission.OrderId.ToString(),
            submission.State.ToWire(),
            submission.RunId?.ToString(),
            SubmissionMessage(submission),
            receipts.Select(r => r.ToDto()).ToList());

    public static OrderStatusDto ToStatusDto(this OrderView view) =>
        new(view.OrderId.ToString(),
            view.Display.State.ToWire(),
            view.Display.IsFinished,
            string.IsNullOrWhiteSpace(view.Progress.CurrentLabel) ? DefaultStep(view.Display) : view.Progress.CurrentLabel,
            (int)Math.Max(0, Math.Round(view.Progress.Elapsed.TotalSeconds)),
            view.CurrentRun?.Version,
            view.CurrentRun?.RunId.ToString(),
            view.Warnings.ToDtos(),
            view.Display.IsFinished ? 0 : NextCheckWhileWorkingSeconds,
            view.CurrentRun?.Outcome?.ToString());

    public static OrderResultDto ToDto(this OrderResult result) =>
        new(result.OrderId.ToString(),
            result.Display.State.ToWire(),
            string.IsNullOrWhiteSpace(result.Text) ? null : result.Text,
            result.WordCount,
            result.Version,
            result.Placeholders,
            result.OpenChecks.Select(c => c.Message).ToList(),
            result.SourcesUsed.Select(s => new OrderSourceUsageDto(s.FileName, s.Mode.ToWire(), s.Characters)).ToList(),
            result.Warnings.ToDtos(),
            string.IsNullOrWhiteSpace(result.Note) ? ResultNote(result) : result.Note);

    private static string SubmissionMessage(OrderSubmission submission)
    {
        var next = submission.State switch
        {
            OrderDisplayState.NeedsInput =>
                "The order needs answers from the person before writing can start. Call get_order_status to see what is needed.",
            OrderDisplayState.Stopped or OrderDisplayState.Failed =>
                "The order did not start. Call get_order_status for details.",
            _ =>
                "The order has started. The text is usually ready in 1–5 minutes, with extraCareful up to about 10. " +
                "Call get_order_status with waitSeconds to wait for it, then get_order_result.",
        };
        return submission.Notices.Count == 0 ? next : $"{next} {string.Join(" ", submission.Notices)}";
    }

    private static string DefaultStep(OrderDisplay display) => display.State switch
    {
        OrderDisplayState.Preparing => "Getting ready",
        OrderDisplayState.NeedsInput => "Needs answers",
        OrderDisplayState.Queued => "Waiting to start",
        OrderDisplayState.Working => "Writing",
        OrderDisplayState.Failed => "No text",
        OrderDisplayState.Stopped => "Stopped",
        _ => "Done",
    };

    private static string? ResultNote(OrderResult result)
    {
        var hasText = !string.IsNullOrWhiteSpace(result.Text);
        var fillIn = result.Placeholders.Count > 0
            ? " Fill in the placeholders in square brackets before using the text."
            : string.Empty;

        if (!result.Display.IsFinished)
            return "The text is not ready yet. Call get_order_status with waitSeconds and ask again when done is true.";

        return result.Display.State switch
        {
            OrderDisplayState.DeliveredWithOpenPoints =>
                "The text is ready, but some points are open: see openChecks and tell the person what to check before using it." + fillIn,
            OrderDisplayState.Stopped when hasText =>
                "The order was stopped. This is the last text written before it stopped." + fillIn,
            OrderDisplayState.Stopped =>
                "The order was stopped before a text was written.",
            OrderDisplayState.Failed =>
                "No text was written. Tell the person; do not write a replacement text yourself.",
            _ when fillIn.Length > 0 => fillIn.Trim(),
            _ => null,
        };
    }
}
