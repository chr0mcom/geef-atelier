using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;

namespace Geef.Atelier.Mcp.Models;

/// <summary>Maps the order service results to the MCP answers.</summary>
internal static class OrderDtoMappings
{
    public const int NextCheckWhileWorkingSeconds = 10;

    public const string StillReadingMessage = "Still reading the request. Call get_order_status with waitSeconds=25.";

    public const string WritingStartedMessage =
        "Writing has started. Call get_order_status with waitSeconds, then get_order_result when done is true.";

    public static OrderSourceDto ToDto(this SourceReceipt receipt) =>
        new(receipt.FileName, receipt.PageCount, receipt.CharCount, receipt.Quality.ToWire(),
            receipt.Warnings.Select(w => w.Message).ToList());

    public static QuestionDto ToDto(this ClarifyQuestion question) =>
        new(question.Id,
            question.Slot.ToWire(),
            question.Text,
            question.Options.Select(o => new QuestionOptionDto(o.Id, o.Label)).ToList(),
            question.DefaultOptionId,
            question.AllowFreeText);

    public static AssumptionDto ToDto(this IntakeAssumption assumption) =>
        new(assumption.Id, assumption.Text, assumption.Confidence == AssumptionConfidence.Unsure);

    public static OrderSubmittedDto ToDto(this OrderSubmission submission, IReadOnlyList<SourceReceipt> receipts) =>
        Submitted(submission.OrderId, submission.State, submission.RunId, submission.Clarification, submission.Notices, receipts);

    /// <summary>The answer of <c>submit_order</c> after it waited for the intake: built from the current order view.</summary>
    public static OrderSubmittedDto ToSubmittedDto(this OrderView view, IReadOnlyList<string> notices, IReadOnlyList<SourceReceipt> receipts) =>
        Submitted(view.OrderId, view.Display.State, view.CurrentRun?.RunId, view.Clarification, notices, receipts);

    public static OrderStatusDto ToStatusDto(this OrderView view)
    {
        var needsInput = view.Display.State == OrderDisplayState.NeedsInput;
        var clarification = needsInput ? view.Clarification : null;
        return new OrderStatusDto(
            view.OrderId.ToString(),
            view.Display.State.ToWire(),
            view.Display.IsFinished,
            string.IsNullOrWhiteSpace(view.Progress.CurrentLabel) ? DefaultStep(view.Display) : view.Progress.CurrentLabel,
            (int)Math.Max(0, Math.Round(view.Progress.Elapsed.TotalSeconds)),
            view.CurrentRun?.Version,
            view.CurrentRun?.RunId.ToString(),
            view.Warnings.ToDtos(),
            view.Display.IsFinished || needsInput ? 0 : NextCheckWhileWorkingSeconds,
            view.CurrentRun?.Outcome?.ToString(),
            clarification?.Understanding,
            clarification?.Assumptions.Select(a => a.ToDto()).ToList(),
            needsInput ? Questions(view.Clarification) : null,
            clarification?.Language,
            needsInput ? NeedsInputMessage(Questions(view.Clarification).Count) : null);
    }

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
            string.IsNullOrWhiteSpace(result.Note) ? ResultNote(result) : result.Note,
            result.Assumptions?.Select(a => a.ToDto()).ToList());

    public static OrderAnsweredDto ToAnsweredDto(this OrderSubmission submission)
    {
        var next = submission.State switch
        {
            OrderDisplayState.Queued or OrderDisplayState.Working => WritingStartedMessage,
            OrderDisplayState.Preparing => StillReadingMessage,
            OrderDisplayState.NeedsInput => NeedsInputMessage(Questions(submission.Clarification).Count),
            OrderDisplayState.Delivered or OrderDisplayState.DeliveredWithOpenPoints =>
                "The order is already done. Call get_order_result.",
            OrderDisplayState.Stopped => "The order was stopped, so nothing will be written.",
            _ => "No text was written. Call get_order_status for details.",
        };
        return new OrderAnsweredDto(
            submission.OrderId.ToString(),
            submission.State.ToWire(),
            submission.State == OrderDisplayState.NeedsInput ? null : submission.RunId?.ToString(),
            WithNotices(next, submission.Notices));
    }

    /// <summary>The hint of a <c>needs_input</c> answer for <paramref name="questionCount"/> open questions.</summary>
    public static string NeedsInputMessage(int questionCount) => questionCount switch
    {
        <= 0 => "Atelier is waiting for answers before writing but has no open question to show. Call answer_order with skip=true to start writing.",
        1 => NeedsInputMessageFor("1 short question"),
        _ => NeedsInputMessageFor($"{questionCount} short questions"),
    };

    private static string NeedsInputMessageFor(string questions) =>
        $"Before writing, Atelier has {questions}. Ask the person in your own words, show the answer options, and say that skipping is fine. " +
        "Then call answer_order with their answers (origin \"user\"), or with skip=true.";

    private static OrderSubmittedDto Submitted(
        Guid orderId,
        OrderDisplayState state,
        Guid? runId,
        ClarificationView? clarification,
        IReadOnlyList<string> notices,
        IReadOnlyList<SourceReceipt> receipts)
    {
        var needsInput = state == OrderDisplayState.NeedsInput;
        var questions = needsInput ? Questions(clarification) : null;
        return new OrderSubmittedDto(
            orderId.ToString(),
            state.ToWire(),
            needsInput ? null : runId?.ToString(),
            WithNotices(SubmissionMessage(state, questions?.Count ?? 0), notices),
            receipts.Select(r => r.ToDto()).ToList(),
            clarification?.Understanding,
            clarification?.Assumptions.Select(a => a.ToDto()).ToList(),
            questions,
            clarification?.Language);
    }

    private static IReadOnlyList<QuestionDto> Questions(ClarificationView? clarification) =>
        clarification?.Questions.Select(q => q.ToDto()).ToList() ?? [];

    private static string SubmissionMessage(OrderDisplayState state, int questionCount) => state switch
    {
        OrderDisplayState.Preparing => StillReadingMessage,
        OrderDisplayState.NeedsInput => NeedsInputMessage(questionCount),
        OrderDisplayState.Stopped or OrderDisplayState.Failed =>
            "The order did not start. Call get_order_status for details.",
        _ =>
            "The order has started. The text is usually ready in 1–5 minutes, with extraCareful up to about 10. " +
            "Call get_order_status with waitSeconds to wait for it, then get_order_result.",
    };

    private static string WithNotices(string message, IReadOnlyList<string> notices) =>
        notices.Count == 0 ? message : $"{message} {string.Join(" ", notices)}";

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
