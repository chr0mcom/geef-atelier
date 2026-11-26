using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Profiles;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Application.Orders;

/// <summary>
/// What the briefing of the next run is built from. <see cref="AssumptionsShown"/> is true only when the person saw the card with
/// the assumptions before the start (the run starts from <see cref="OrderState.NeedsInput"/>). <see cref="NotChecked"/> is true when an
/// order of the intake starts before its request was checked (skipped while reading, the clarifier failed or timed out); orders without
/// the intake step never set it.
/// </summary>
public sealed record OrderBriefInput(OrderEntity Order, IntakeRecord? Record, IReadOnlyList<OrderAnswer> Answers, UserProfileEntity? Profile, bool AssumptionsShown = false,
    bool NotChecked = false);

/// <summary>The briefing text and the names of the sections it contains, in order.</summary>
public sealed record IntakeBrief(string Text, IReadOnlyList<string> Sections);

/// <summary>
/// Builds the run briefing from an order. Stage 1: the request verbatim. Stage 2 (<c>Intake:BriefMode</c>): request plus answers,
/// corrections and the assumptions of the intake (as defaults, never as checks), or a prepared brief. Stage 3
/// (<c>Intake:ContentAssumptions</c>): assumptions about facts of the case are listed as not given by the person instead of as
/// defaults. The assumptions are called shown to the person only when the card was really shown
/// (<see cref="OrderBriefInput.AssumptionsShown"/>). The request always comes first and unchanged; everything else follows after
/// <see cref="BriefingSections.Separator"/>. Questions the person left open (<see cref="OpenQuestions.IsLeftOpen"/>: skipped, not
/// answered in time, "You decide") and the questions the gate held back (<see cref="IntakeRecord.HeldBack"/>) are listed under their own
/// heading, which forbids deciding for the person what would commit them; an order of the intake that starts unchecked
/// (<see cref="OrderBriefInput.NotChecked"/>) gets the same rule as one general line. A briefing without either stays exactly as before.
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
    public const string OpenFactsSection = "open-facts";
    public const string OpenQuestionsSection = "open-questions";
    public const string NotCheckedSection = "not-checked";

    public const string AnswersHeading = "Answers the person gave before writing:";
    public const string CorrectionsHeading = "Corrections to assumptions:";
    public const string AssumptionsHeadingStart = "Assumptions shown to the person, not confirmed";
    public const string AssumptionsHeading = AssumptionsHeadingStart + " (the answers and corrections of the person above always take precedence; use the assumptions only where neither they nor the request or the files say otherwise; never state them as facts):";
    public const string AssumptionsNotShownHeadingStart = "Assumptions made before writing; the person has not seen them";
    public const string AssumptionsNotShownHeading = AssumptionsNotShownHeadingStart + " (use them only where neither the request nor the files say otherwise; never state them as facts):";
    public const string OpenFactsHeadingStart = "Not given by the person";
    public const string OpenFactsHeading = OpenFactsHeadingStart + " (never state these as facts in the text; where the text needs one of them, write a placeholder in square brackets or leave it out):";
    public const string KeepDecisionsOpenInOneWay =
        "never decide it for the person. Still write a complete text that can be sent as it is, and keep each such decision open in exactly one of two ways, never both: "
        + "either do not decide yet – confirm receipt, ask back or say when the person will reply (prefer this where an interim reply makes sense) – "
        + "or put the decision into exactly one clearly visible placeholder in square brackets that holds everything that depends on it, with both possible answers in the language of the text. "
        + "No sentence outside that placeholder may assume a yes or a no, not even in passing, for example by looking forward to it or by thanking for an acceptance. "
        + "Name each open decision among your open points.";
    public const string OpenQuestionsHeading = "Questions the person did not answer (the person skipped them, left them to you, or was not asked). "
        + "The request, the answers and corrections of the person and any change the person asks for take precedence: where one of them settles one of these questions, follow the person. "
        + "Otherwise, where the answer would commit the person – for example to accept or decline, order, cancel, pay or confirm attendance – " + KeepDecisionsOpenInOneWay + " "
        + "Never invent facts for these questions either. For anything else, choose what fits the request best:";
    public const string NotCheckedHeading = "The request was not checked for missing details before writing. "
        + "The request and any change the person asks for take precedence: where one of them settles a decision, follow the person. "
        + "Otherwise, where the text would need a decision only the person can make – for example to accept or decline, order, cancel, pay or confirm attendance – " + KeepDecisionsOpenInOneWay;
    public const string OpenQuestionsHeadingFirstVersion = "Questions the person did not answer (the person skipped them, left them to you, or was not asked). "
        + "The request, the answers and corrections of the person and any change the person asks for take precedence: where one of them settles one of these questions, follow the person. "
        + "Otherwise, where the answer would commit the person – for example to accept or decline, order, cancel, pay or confirm attendance – never decide it for the person: keep it open, for example by confirming receipt, asking back or asking for time, or write a clearly visible placeholder in square brackets, and name the open decision among your open points. "
        + "Never invent facts for these questions either. For anything else, choose what fits the request best:";
    public const string NotCheckedHeadingFirstVersion = "The request was not checked for missing details before writing. "
        + "Where the text would need a decision only the person can make – for example to accept or decline, order, cancel, pay or confirm attendance – never decide it for the person: keep it open, for example by confirming receipt, asking back or asking for time, or write a clearly visible placeholder in square brackets, and name the open decision among your open points. "
        + "Where the request or a change the person asks for settles it, follow the person.";
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

        var questions = record?.Questions ?? [];
        var openQuestions = questions.Where(q => q.IsLeftOpen(answers)).ToList();
        var answered = questions.Where(q => !openQuestions.Contains(q)).Select(q => q.Text.SameQuestionKey()).ToHashSet(StringComparer.Ordinal);
        var openLines = openQuestions.Select(q => OneLine(q.Text))
            .Concat((record?.HeldBack ?? []).Select(OneLine).Where(l => !answered.Contains(l.SameQuestionKey())))
            .Where(l => l.Length > 0)
            .DistinctBy(l => l.SameQuestionKey(), StringComparer.Ordinal)
            .ToList();
        var answerLines = questions
            .Where(q => answers.ContainsKey(q.Id) && !openQuestions.Contains(q))
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

        if (openLines.Count > 0)
        {
            intake.Add(OpenQuestionsHeading);
            intake.AddRange(openLines.Select(l => "- " + l));
            sections.Add(OpenQuestionsSection);
        }

        if (input.NotChecked && record?.Output is null)
        {
            intake.Add(NotCheckedHeading);
            sections.Add(NotCheckedSection);
        }

        if (record?.Output is not null)
        {
            var remaining = (record?.Assumptions ?? []).Where(a => !corrected.Contains(a.Id)).ToList();
            var openFacts = options.Value.ContentAssumptions == ContentAssumptionMode.OpenPoints;
            var form = remaining.Where(a => !openFacts || !a.Topic.IsContent()).Select(a => "- " + OneLine(a.Text)).ToList();
            var open = openFacts ? remaining.Where(a => a.Topic.IsContent()).Select(a => "- " + OneLine(a.Text)).ToList() : [];
            if (form.Count > 0)
            {
                intake.Add(input.AssumptionsShown ? AssumptionsHeading : AssumptionsNotShownHeading);
                intake.AddRange(form);
                sections.Add(AssumptionsSection);
            }
            if (open.Count > 0)
            {
                intake.Add(OpenFactsHeading);
                intake.AddRange(open);
                sections.Add(OpenFactsSection);
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
