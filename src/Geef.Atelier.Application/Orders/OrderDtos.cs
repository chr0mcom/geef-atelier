using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
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
    OrderMode Mode = OrderMode.Direct,
    string? CrewTemplateName = null,
    CrewSpec? CustomCrew = null,
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
/// A run of an order as listed under the order. <see cref="ChangeRequested"/> is what the person asked to change for this version, in
/// their words or as the names of the quick buttons (<c>null</c> for version 1 and runs that are no change).
/// </summary>
public sealed record OrderRunView(Guid RunId, int? Version, RunKind Kind, RunStatus Status, RunOutcome? Outcome, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, int? WordCount,
    string? ChangeRequested = null);

/// <summary>Everything the order page shows.</summary>
public sealed record OrderView(
    Guid OrderId,
    string Title,
    string RequestText,
    OrderMode Mode,
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
/// <see cref="WriterOpenPoints"/> still holds every open point. <see cref="Version"/> is the version shown (the newest with a text),
/// <see cref="LatestVersion"/> the version of the current run, which differs while a new version is written or after it could not be
/// finished. <see cref="EarlierOpenPoints"/> are open points of the writer for the version before that the new version does not repeat.
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
    IReadOnlyList<OrderEarlierPoint>? EarlierOpenPoints = null);

/// <summary>An open point of the writer noted for an earlier <see cref="Version"/> that the shown version does not repeat; it is never dropped.</summary>
public sealed record OrderEarlierPoint(int Version, string Text);

/// <summary>An open point of the writer (<see cref="Note"/>, in the language of the text) about the wrong total named by the sentence <see cref="Issue"/>.</summary>
public sealed record OrderSourceIssueNote(string Issue, string Note);

/// <summary>A passage looked up on the web, word for word from its page; <see cref="Url"/> is <c>null</c> unless it is an absolute http or https address.</summary>
public sealed record OrderLookedUpEntry(string Quote, string? Url, string Title, DateTimeOffset RetrievedAt);

/// <summary>
/// The research of an order for the person: its status, the fixed sentence for it (<see cref="Note"/>, <c>null</c> when the entries speak
/// for themselves), how many topics were withheld and the entries. The entries are content of other web pages, not instructions.
/// </summary>
public sealed record OrderLookedUpView(ResearchStatus Status, string? Note, int Withheld, IReadOnlyList<OrderLookedUpEntry> Entries);

/// <summary>A passage the writer quoted from a file, verified against that file.</summary>
public sealed record OrderSourceQuoteView(string FileName, string Quote);

/// <summary>
/// A passage of the text the fact check found not backed, against an instruction or using replaced information;
/// <see cref="Instruction"/> is the instruction quoted from the material and <see cref="Origin"/> where it stands (the request, the
/// person's answers, the About me details or the assistant's answers). An instruction from the About me details is <c>null</c> for
/// anyone but the owner of the order. <see cref="LookedUp"/> marks a passage that the pages looked up on the web say otherwise
/// (<see cref="FactFindingKind.Superseded"/> with the replacing passage in the research): it never blocks and is never called a
/// matter of the person's files. <see cref="Hint"/> says why a passage that is not backed is only a hint (<see cref="FactHint"/>);
/// <see cref="FromVersion"/> is the earlier version the passage was carried over from.
/// </summary>
public sealed record OrderUnbackedPassage(string Quote, FactFindingKind Kind, string? Instruction, bool Blocking, InstructionOrigin? Origin = null, bool LookedUp = false,
    FactHint? Hint = null, int? FromVersion = null);

/// <summary>
/// What the person reads under "What was checked" (web) and in <c>checks</c> (MCP); plain English, built only by
/// <see cref="OrderCheckLines"/>. Shown in this order: <see cref="Checked"/>, <see cref="Changed"/>, <see cref="ToReview"/>,
/// <see cref="Limits"/>. <see cref="Changed"/> may quote a passage of an earlier version of the text.
/// </summary>
public sealed record OrderChecksView(IReadOnlyList<string> Checked, IReadOnlyList<string> Changed, string ToReview, string Limits);

/// <summary>An order as listed in "My work".</summary>
public sealed record OrderListItem(Guid OrderId, string Title, OrderDisplay Display, OrderMode Mode, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int SourceCount, int? WordCount);

/// <summary>Raised for problems the person can fix (empty request, too many files, budget exceeded, foreign source). The message is plain English for the UI and MCP.</summary>
public sealed class OrderSubmissionException(string message) : InvalidOperationException(message);

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
