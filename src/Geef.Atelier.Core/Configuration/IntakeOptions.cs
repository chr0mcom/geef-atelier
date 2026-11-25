using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Core.Configuration;

/// <summary>Behaviour of the intake that clarifies orders before writing (section <c>Intake</c>).</summary>
public sealed class IntakeOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Intake";

    /// <summary>How the briefing of a clarified order is built.</summary>
    public BriefMode BriefMode { get; set; } = BriefMode.AppendAnswers;

    /// <summary>Hard limit of the clarifier call.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(40);

    /// <summary>An order waiting this long without a claim starts without clarification.</summary>
    public TimeSpan QueueTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>A claim older than this counts as abandoned.</summary>
    public TimeSpan ClaimLease { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>Most claims per order (and per shadow row).</summary>
    public int MaxAttempts { get; set; } = 2;

    /// <summary>Most clarifier calls at the same time.</summary>
    public int MaxConcurrent { get; set; } = 2;

    /// <summary>Longest wait of the worker between two polls.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Most questions per order (one for a revision).</summary>
    public int MaxQuestions { get; set; } = 3;

    /// <summary>Requests longer than this are not clarified.</summary>
    public int SkipAboveChars { get; set; } = 8_000;

    /// <summary>How long submit_order waits for the clarification, in seconds.</summary>
    public int SubmitWaitSeconds { get; set; } = 30;

    /// <summary>Unanswered questions start the order with the assumptions after this time; zero turns it off.</summary>
    public TimeSpan NeedsInputAutoStartAfter { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Characters from the start of each file shown to the clarifier.</summary>
    public int ExcerptHeadChars { get; set; } = 2_000;

    /// <summary>Characters from the end of each file shown to the clarifier.</summary>
    public int ExcerptTailChars { get; set; } = 1_000;

    /// <summary>Most excerpt characters over all files.</summary>
    public int ExcerptTotalChars { get; set; } = 12_000;

    /// <summary>
    /// Output limit of the clarifier call; a safety cap, not a target. The largest answer the parser keeps (8 gaps, each with
    /// question and six options, about 3,500 tokens) plus reasoning on effort low must fit, because a cut-off answer is invalid
    /// JSON and the order would start without clarification. Only the tokens actually written count; the gateway routes of the
    /// CLIs do not pass the limit on at all.
    /// </summary>
    public int MaxTokens { get; set; } = 8_192;

    /// <summary>Reasoning effort sent with the clarifier call.</summary>
    public string ReasoningEffort { get; set; } = "low";

    /// <summary>Whether the gateway may answer from another CLI when the first one fails.</summary>
    public bool AllowFailover { get; set; } = true;

    /// <summary>Most shadow clarifications per UTC day.</summary>
    public int ShadowDailyCap { get; set; } = 50;

    /// <summary>Longest free-text answer or assumption correction.</summary>
    public int MaxFreeTextChars { get; set; } = 500;

    /// <summary>How assumptions about facts of the case (<see cref="GapTopicKinds.IsContent"/>) reach the writer.</summary>
    public ContentAssumptionMode ContentAssumptions { get; set; } = ContentAssumptionMode.OpenPoints;

    /// <summary>Whether the gate drops answer options with a date, month or weekday that the request and the files do not name (<see cref="GateDecision.DroppedGuessedDate"/>).</summary>
    public bool DropGuessedDateOptions { get; set; }
}
