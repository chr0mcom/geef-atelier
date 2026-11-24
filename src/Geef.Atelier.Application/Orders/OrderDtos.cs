using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
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

/// <summary>A run of an order as listed under the order.</summary>
public sealed record OrderRunView(Guid RunId, int? Version, RunKind Kind, RunStatus Status, RunOutcome? Outcome, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, int? WordCount);

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

/// <summary>The delivered text of an order with what the person should check before using it.</summary>
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
    IReadOnlyList<IntakeAssumption>? Assumptions = null);

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
