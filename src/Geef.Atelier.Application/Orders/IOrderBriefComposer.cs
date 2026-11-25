using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Profiles;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Application.Orders;

/// <summary>What the briefing of the next run is built from.</summary>
public sealed record OrderBriefInput(OrderEntity Order, IntakeRecord? Record, IReadOnlyList<OrderAnswer> Answers, UserProfileEntity? Profile);

/// <summary>The briefing text and the names of the sections it contains, in order.</summary>
public sealed record IntakeBrief(string Text, IReadOnlyList<string> Sections);

/// <summary>
/// Builds the run briefing from an order. Stage 1: the request verbatim. Stage 2 (<c>Intake:BriefMode</c>): request plus answers,
/// corrections and the assumptions shown to the person (as defaults, never as checks), or a prepared brief. The request always
/// comes first and unchanged; everything else follows after <see cref="BriefingSections.Separator"/>.
/// </summary>
public interface IOrderBriefComposer
{
    /// <summary>The briefing of the next run. Without record, answers and profile exactly the request (stage 1, bit-identical).</summary>
    IntakeBrief Compose(OrderBriefInput input);
}

internal sealed class IntakeBriefComposer(IOptions<IntakeOptions> options) : IOrderBriefComposer
{
    public const string RequestSection = "request";
    public const string UnderstandingSection = "understanding";
    public const string AnswersSection = "answers";
    public const string CorrectionsSection = "corrections";
    public const string AssumptionsSection = "assumptions";
    public const string ProfileSection = "profile";

    public const string AnswersHeading = "Answers the person gave before writing:";
    public const string CorrectionsHeading = "Corrections to assumptions:";
    public const string AssumptionsHeadingStart = "Assumptions shown to the person, not confirmed";
    public const string AssumptionsHeading = AssumptionsHeadingStart + " (the answers and corrections of the person above always take precedence; use the assumptions only where neither they nor the request or the files say otherwise; never state them as facts):";
    public const string RevisionQuestion = "Improve the attached file chosen as the text to revise?";
    public const string RevisionYes = "Yes, improve that file";
    public const string RevisionNo = "No, write something new and use the file only as material";
    public const string ProfileHeading = "About the person (use only where it fits the text; never invent anything beyond it):";
    public const string UnderstandingLead = "What will be written: ";
    public const string LeftToYou = "left to you";
    public const string ByAssistant = " (answered by the assistant)";

    public IntakeBrief Compose(OrderBriefInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Order);

        var prepared = options.Value.BriefMode == BriefMode.PreparedBrief;
        var record = input.Record;
        var answers = (input.Answers ?? []).GroupBy(a => a.QuestionId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var sections = new List<string> { RequestSection };
        var intake = new List<string>();

        if (prepared && record?.Output is { } output && !string.IsNullOrWhiteSpace(output.Understanding))
        {
            intake.Add(UnderstandingLead + OneLine(output.Understanding));
            if (!string.IsNullOrWhiteSpace(output.TextType))
                intake.Add("Text type: " + OneLine(output.TextType));
            sections.Add(UnderstandingSection);
        }

        var answerLines = (record?.Questions ?? [])
            .Where(q => answers.ContainsKey(q.Id))
            .Select(q => $"- {OneLine(q.Text)} → {Describe(q, answers[q.Id])}")
            .ToList();
        if (record?.RevisionSourceId is not null && answers.TryGetValue(RevisionAnswer.QuestionId, out var revision)
            && revision.OptionId is RevisionAnswer.Yes or RevisionAnswer.No)
            answerLines.Add($"- {RevisionQuestion} → {(revision.OptionId == RevisionAnswer.No ? RevisionNo : RevisionYes)}{Mark(revision)}");
        if (answerLines.Count > 0)
        {
            intake.Add(AnswersHeading);
            intake.AddRange(answerLines);
            sections.Add(AnswersSection);
        }

        var corrected = new HashSet<string>(StringComparer.Ordinal);
        var correctionLines = new List<string>();
        foreach (var assumption in record?.Assumptions ?? [])
        {
            if (!answers.TryGetValue(assumption.Id, out var answer) || string.IsNullOrWhiteSpace(answer.FreeText))
                continue;
            corrected.Add(assumption.Id);
            correctionLines.Add($"- {OneLine(assumption.Text)} → {OneLine(answer.FreeText)}{Mark(answer)}");
        }

        if (correctionLines.Count > 0)
        {
            intake.Add(CorrectionsHeading);
            intake.AddRange(correctionLines);
            sections.Add(CorrectionsSection);
        }

        if (record?.Output is not null)
        {
            var remaining = (record?.Assumptions ?? []).Where(a => !corrected.Contains(a.Id)).Select(a => "- " + OneLine(a.Text)).ToList();
            if (remaining.Count > 0)
            {
                intake.Add(AssumptionsHeading);
                intake.AddRange(remaining);
                sections.Add(AssumptionsSection);
            }
        }

        var blocks = new List<string>();
        if (intake.Count > 0)
            blocks.Add(string.Join('\n', intake));
        if (ProfilePrompt.Render(input.Profile) is { } profile)
        {
            blocks.Add(ProfileHeading + "\n" + profile);
            sections.Add(ProfileSection);
        }

        var text = blocks.Count == 0
            ? input.Order.RequestText
            : input.Order.RequestText + BriefingSections.Separator + string.Join("\n\n", blocks);
        return new IntakeBrief(text, sections);
    }

    private static string Describe(ClarifyQuestion question, OrderAnswer answer)
    {
        var free = string.IsNullOrWhiteSpace(answer.FreeText) ? null : OneLine(answer.FreeText);
        if (answer.DecideForMe)
            return LeftToYou + Mark(answer);

        var label = question.Options.FirstOrDefault(o => o.Id == answer.OptionId)?.Label;
        var value = (label, free) switch
        {
            ({ } l, { } f) => $"{OneLine(l)} – \"{f}\"",
            ({ } l, null) => OneLine(l),
            (null, { } f) => $"\"{f}\"",
            _ => null,
        };
        return value is null ? LeftToYou : value + Mark(answer);
    }

    private static string Mark(OrderAnswer answer) => answer.Origin == AnswerOrigin.Client ? ByAssistant : string.Empty;

    private static string OneLine(string text) => string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
