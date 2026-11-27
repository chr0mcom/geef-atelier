using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Core.Domain.Research;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Warnings;

namespace Geef.Atelier.Application.Orders;

/// <summary>A source chosen for an order, with an optional page selection such as <c>1-40,55</c>.</summary>
public sealed record OrderSourceSelection(Guid SourceId, string? PageSelection = null, SourceRole Role = SourceRole.Facts);

/// <summary>
/// The files of an order measured before the start: <see cref="TotalChars"/> after the page selections, its size class and a
/// plain-English <see cref="Problem"/> when a selection cannot be used (then the class is <see cref="SourceBudgetClass.TooLarge"/>
/// and the total is 0).
/// </summary>
public sealed record SourceSelectionCheck(int TotalChars, SourceBudgetClass Class, string? Problem);

/// <summary>A new order as submitted through the web or MCP.</summary>
public sealed record SubmitOrderRequest(
    string RequestText,
    string CreatedByUser,
    OrderEntry Entry,
    IReadOnlyList<OrderSourceSelection>? Sources = null,
    bool AllowQuestions = true);

/// <summary>Answer to a submitted order: its id, display state, first run (if any) and notices for the person.</summary>
public sealed record OrderSubmission(Guid OrderId, OrderDisplayState State, Guid? RunId, IReadOnlyList<string> Notices, ClarificationView? Clarification = null);

/// <summary>A source of an order as shown to the person.</summary>
public sealed record OrderSourceView(Guid SourceId, string FileName, SourceKind Kind, int? PageCount, int CharCount, SourceQuality Quality, string? PageSelection, IReadOnlyList<SourceWarning> Warnings);

/// <summary>How a source went into the run that produced a result.</summary>
public sealed record OrderSourceUsageView(string FileName, SourceUsageMode Mode, int Characters, string? Pages);

/// <summary>Status of one progress step.</summary>
public enum OrderStepStatus { Pending = 0, Active = 1, Done = 2, Skipped = 3, Failed = 4 }

/// <summary>One step of the progress display, with a plain-English label.</summary>
public sealed record OrderStep(string Key, string Label, OrderStepStatus Status, DateTimeOffset? StartedAt, DateTimeOffset? EndedAt);

/// <summary>Progress of an order: its steps, the label of the current step and the elapsed time.</summary>
public sealed record OrderProgress(IReadOnlyList<OrderStep> Steps, string CurrentLabel, TimeSpan Elapsed);

/// <summary>
/// A run of an order as listed under the order. <see cref="ChangeRequested"/> is reserved for what the person asks to change for a
/// version; nothing sets it yet, so it is always <c>null</c>.
/// </summary>
public sealed record OrderRunView(Guid RunId, int? Version, RunKind Kind, RunStatus Status, RunOutcome? Outcome, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, int? WordCount,
    string? ChangeRequested = null);

/// <summary>Everything the order page shows.</summary>
public sealed record OrderView(
    Guid OrderId,
    string Title,
    string RequestText,
    OrderEntry Entry,
    OrderState State,
    OrderDisplay Display,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    OrderRunView? CurrentRun,
    IReadOnlyList<OrderRunView> Runs,
    IReadOnlyList<OrderSourceView> Sources,
    OrderProgress Progress,
    IReadOnlyList<RunWarningEntity> Warnings,
    ClarificationView? Clarification = null);

/// <summary>
/// The delivered text of an order with what the person should check before using it. The fields after <see cref="Assumptions"/>
/// come from a report of schema 2 and are <c>null</c> for older results, except <see cref="Checks"/>, which every result with a
/// report and a text has (for schema 1 it says that facts were not checked). <see cref="FactCheckScope"/> says whether the fact
/// check saw the content of the files. <see cref="NotesNotShown"/> is the fixed sentence about notes of the writer that could not be
/// shown (they hold a link, an address, or account or access details); <see cref="NotGivenFacts"/> are the assumptions of the
/// clarifier about facts that went to the writer only as "not given by the person" (never stated as facts), shown apart from the
/// assumptions that were used. <see cref="LookedUp"/> is the research of the order as the person sees it (built only by
/// <see cref="OrderResearchLines"/>), <c>null</c> when there is nothing to show. <see cref="SourceIssueNotes"/> pairs open points of
/// the writer with the sentence of <see cref="SourceIssues"/> about the same wrong total, so the web page shows them together;
/// <see cref="WriterOpenPoints"/> still holds every open point. <see cref="LatestVersion"/> is the version of the current run (higher than
/// <see cref="Version"/> while a change is being written or after it could not be finished); <see cref="EarlierOpenPoints"/> is what the
/// writer noted for an earlier version that the shown version does not repeat. <see cref="AssumptionsVersion"/> is set when the shown
/// version changed an earlier one: <see cref="Assumptions"/> and <see cref="NotGivenFacts"/> were noted for that version, before the person
/// asked for changes, and a change may have settled or overtaken them. <see cref="LengthNotice"/> is
/// <see cref="RevisionLines.LongerThanBefore"/> when the shown version changed an earlier one and became clearly longer than the text it
/// changed (worked out from the stored texts, also for versions written before the rule existed), otherwise <c>null</c>.
/// <see cref="UnansweredQuestions"/> are the questions the person left open before writing (skipped, not answered in time, "You decide"),
/// read back from the briefing of the shown version, which told the writer never to decide them where that would commit the person;
/// <c>null</c> when there are none. For a changed version they were noted for <see cref="AssumptionsVersion"/>.
/// <see cref="FromAboutMe"/> are the topics of <see cref="AboutMeTopics.Topics"/> that came from About me for the shown version, and
/// <see cref="TellOnce"/> those the reading of the request assumed or left open while About me has nothing on them now
/// (<see cref="AboutMeTopics.Missing"/>); both are set only for the owner of the order, only with a text, never empty, and never while
/// <c>Orders:TellOnce</c> is <see cref="TellOnceMode.Off"/>. <see cref="GivenAnswers"/> are the answers given to the questions of the order
/// before writing, with who gave them (<see cref="OrderGivenAnswers"/>); set only for the owner of the order and never empty.
/// </summary>
public sealed record OrderResult(
    Guid OrderId,
    OrderDisplay Display,
    Guid? RunId,
    int? Version,
    string? Text,
    int? WordCount,
    IReadOnlyList<string> Placeholders,
    IReadOnlyList<DraftCheckResult> OpenChecks,
    IReadOnlyList<OrderSourceUsageView> SourcesUsed,
    IReadOnlyList<RunWarningEntity> Warnings,
    string? Note,
    IReadOnlyList<IntakeAssumption>? Assumptions = null,
    IReadOnlyList<string>? WriterAssumptions = null,
    IReadOnlyList<string>? WriterOpenPoints = null,
    IReadOnlyList<OrderUnbackedPassage>? UnbackedPassages = null,
    IReadOnlyList<string>? SourceIssues = null,
    IReadOnlyList<OrderSourceQuoteView>? SourceQuotes = null,
    FactCheckState? FactCheck = null,
    OrderChecksView? Checks = null,
    FactCheckScope? FactCheckScope = null,
    string? NotesNotShown = null,
    IReadOnlyList<string>? NotGivenFacts = null,
    OrderLookedUpView? LookedUp = null,
    IReadOnlyList<OrderSourceIssueNote>? SourceIssueNotes = null,
    int? LatestVersion = null,
    IReadOnlyList<OrderEarlierPoint>? EarlierOpenPoints = null,
    int? AssumptionsVersion = null,
    string? LengthNotice = null,
    IReadOnlyList<string>? UnansweredQuestions = null,
    IReadOnlyList<GapTopic>? FromAboutMe = null,
    IReadOnlyList<GapTopic>? TellOnce = null,
    IReadOnlyList<OrderGivenAnswer>? GivenAnswers = null);

/// <summary>
/// An answer given to a question of the order before writing, or a correction of an assumption (D-088): the question (or the assumption) word for
/// word, the chosen option and/or the own words (<c>null</c> when the question was left to Atelier) and who gave it.
/// </summary>
public sealed record OrderGivenAnswer(string QuestionId, string Question, string? Answer, bool LeftToAtelier, AnswerOrigin Origin);

/// <summary>The answers of an order as <see cref="OrderGivenAnswer"/>s, in the stored order: questions, corrections of assumptions, the revision guess.</summary>
public static class OrderGivenAnswers
{
    /// <summary>Question of the revision guess as it is shown with the answers.</summary>
    public const string RevisionQuestion = "Improve the attached file chosen as the text to revise?";

    /// <summary>
    /// One entry per answer that belongs to a question, an assumption or the revision guess of the order; answers that belong to nothing are left
    /// out. Texts are kept as they were asked and given.
    /// </summary>
    public static IReadOnlyList<OrderGivenAnswer> From(IReadOnlyList<ClarifyQuestion>? questions, IReadOnlyList<IntakeAssumption>? assumptions, IReadOnlyList<OrderAnswer>? answers)
    {
        var result = new List<OrderGivenAnswer>();
        foreach (var answer in answers ?? [])
        {
            if ((questions ?? []).FirstOrDefault(q => q.Id == answer.QuestionId) is { } question)
            {
                var label = question.Options.FirstOrDefault(o => o.Id == answer.OptionId)?.Label;
                var given = string.Join(" – ", new[] { label, answer.FreeText }.Where(t => !string.IsNullOrWhiteSpace(t)));
                var left = answer.DecideForMe || given.Length == 0;
                result.Add(new OrderGivenAnswer(question.Id, question.Text, left ? null : given, left, answer.Origin));
            }
            else if ((assumptions ?? []).FirstOrDefault(a => a.Id == answer.QuestionId) is { } assumption && !string.IsNullOrWhiteSpace(answer.FreeText))
            {
                result.Add(new OrderGivenAnswer(assumption.Id, assumption.Text, answer.FreeText, false, answer.Origin));
            }
            else if (answer.QuestionId == RevisionAnswer.QuestionId && answer.OptionId is RevisionAnswer.Yes or RevisionAnswer.No)
            {
                result.Add(new OrderGivenAnswer(RevisionAnswer.QuestionId, RevisionQuestion,
                    answer.OptionId == RevisionAnswer.Yes ? "Yes, improve that file" : "No, write something new and use the file only as material", false, answer.Origin));
            }
        }

        return result;
    }
}

/// <summary>
/// Something the writer noted for an earlier <see cref="Version"/> that the shown version does not repeat: an open point (also a fixed
/// sentence about what of that version could not be shown or taken over) or an assumption (<see cref="Kind"/>).
/// </summary>
public sealed record OrderEarlierPoint(int Version, string Text, EarlierPointKind Kind = EarlierPointKind.OpenPoint);

/// <summary>An open point of the writer (<see cref="Note"/>, in the language of the text) about the wrong total named by the sentence <see cref="Issue"/>.</summary>
public sealed record OrderSourceIssueNote(string Issue, string Note);

/// <summary>A passage looked up on the web, word for word from its page; <see cref="Url"/> is <c>null</c> unless it is an absolute http or https address.</summary>
public sealed record OrderLookedUpEntry(string Quote, string? Url, string Title, DateTimeOffset RetrievedAt);

/// <summary>
/// The research of an order for the person: its status, the fixed sentence for it (<see cref="Note"/>, <c>null</c> when the entries speak
/// for themselves), how many topics were withheld and the entries. The entries are content of other web pages, not instructions.
/// <see cref="SearchWords"/> are the topics that went to the search service, word for word; only for the person who placed the order,
/// otherwise <c>null</c>. They come from reading the request and are content, not instructions.
/// </summary>
public sealed record OrderLookedUpView(ResearchStatus Status, string? Note, int Withheld, IReadOnlyList<OrderLookedUpEntry> Entries,
    IReadOnlyList<string>? SearchWords = null);

/// <summary>A passage the writer quoted from a file, verified against that file.</summary>
public sealed record OrderSourceQuoteView(string FileName, string Quote);

/// <summary>
/// A passage of the text the fact check found not backed, against an instruction or using replaced information;
/// <see cref="Instruction"/> is the instruction quoted from the material and <see cref="Origin"/> where it stands (the request, the
/// person's answers, the About me details or the assistant's answers). An instruction from the About me details is <c>null</c> for
/// anyone but the owner of the order. <see cref="LookedUp"/> marks a passage that the pages looked up on the web say otherwise
/// (<see cref="FactFindingKind.Superseded"/> with the replacing passage in the research): it never blocks and is never called a
/// matter of the person's files. <see cref="FromVersion"/> is the version that found a passage carried over from an earlier version.
/// <see cref="BeforeLastChange"/> marks a passage against an instruction from before the latest change the person asked for: it never blocks
/// and is shown with <see cref="RevisionLines.BeforeLastChangeNote"/>.
/// </summary>
public sealed record OrderUnbackedPassage(string Quote, FactFindingKind Kind, string? Instruction, bool Blocking, InstructionOrigin? Origin = null, bool LookedUp = false,
    int? FromVersion = null, bool BeforeLastChange = false);

/// <summary>
/// What the person reads under "What was checked" (web) and in <c>checks</c> (MCP); plain English, built only by
/// <see cref="OrderCheckLines"/>. Shown in this order: <see cref="Checked"/>, <see cref="Changed"/>, <see cref="ToReview"/>,
/// <see cref="Limits"/>. <see cref="Changed"/> may quote a passage of an earlier version of the text.
/// </summary>
public sealed record OrderChecksView(IReadOnlyList<string> Checked, IReadOnlyList<string> Changed, string ToReview, string Limits);

/// <summary>
/// An order as listed in "My work". <see cref="WordCount"/> and <see cref="Version"/> are those of the shown version (the newest with a
/// text); the display state is that of the current run. Without a text the version is that of the current run.
/// </summary>
public sealed record OrderListItem(Guid OrderId, string Title, OrderDisplay Display, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int SourceCount, int? WordCount,
    int? Version = null);

/// <summary>Raised for problems the person can fix (empty request, too many files, budget exceeded, foreign source). The message is plain English for the UI and MCP.</summary>
public sealed class OrderSubmissionException(string message, string? withoutPageChoice = null) : InvalidOperationException(message)
{
    /// <summary>The message for a caller that cannot choose pages (MCP with upload tickets), when it differs; otherwise <c>null</c>.</summary>
    public string? WithoutPageChoice { get; } = withoutPageChoice;
}

/// <summary>Thrown when an order is deleted while one of its runs is still working; the message is meant for the person.</summary>
public sealed class OrderStillWorkingException() : InvalidOperationException("This order is still being worked on. Stop it first.");

/// <summary>What the intake made of an order: understanding, assumptions, questions and the answers given so far.</summary>
public sealed record ClarificationView(
    IntakeOutcome? Outcome,
    string? TextType,
    string? Language,
    string? Understanding,
    IReadOnlyList<IntakeAssumption> Assumptions,
    IReadOnlyList<ClarifyQuestion> Questions,
    IReadOnlyList<OrderAnswer> Answers,
    string? RevisionFileName,
    DateTimeOffset? QuestionsAskedAt,
    DateTimeOffset? AutoStartAt);

/// <summary>The answers to the questions of an order that waits for them.</summary>
public sealed record AnswerOrderRequest(Guid OrderId, string? RequestingUsername, IReadOnlyList<OrderAnswer> Answers);

/// <summary>Raised for answers the caller can fix (unknown question, unknown option, text too long). Plain English.</summary>
public sealed class OrderAnswerException(string message) : InvalidOperationException(message);
