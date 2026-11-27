namespace Geef.Atelier.Core.Domain;

/// <summary>Represents a single text-generation run submitted to the Geef pipeline.</summary>
public sealed class RunEntity
{
    public required Guid Id { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required RunStatus Status { get; init; }
    public required string BriefingText { get; init; }

    public string? CreatedByUser { get; init; }

    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? FinalText { get; init; }
    /// <summary>
    /// Why the run failed or stopped, shown to the user: a fixed plain-English sentence without technical details
    /// (exception texts, upstream answers, host or variable names, paths, technical profile names). Details belong in
    /// the log; same rule as <c>IRunWarningSink.ReportAsync</c>.
    /// </summary>
    public string? ErrorMessage { get; init; }
    public int TokensTotal { get; init; }

    /// <summary>Set to true when a cancellation request has been submitted for this run.</summary>
    public bool CancellationRequested { get; init; }

    /// <summary>Name of the code-only crew template the run was submitted with (<see cref="Crew.SystemCrew.DirectTemplateName"/>).</summary>
    public string? CrewTemplateName { get; init; }

    /// <summary>
    /// JSONB-serialised <see cref="Crew.CrewSnapshot"/> capturing the fully-dereferenced crew
    /// configuration used for this run. Persisted to keep runs reproducible after profiles change.
    /// </summary>
    public string? CrewSnapshot { get; init; }

    // Cost tracking — null while the run is working and when CostTracking:Enabled=false.
    public decimal? TotalCostEur { get; set; }
    public decimal? LlmCostEur { get; set; }

    // Token breakdown — run-level sums of the cached-input / reasoning token subsets across all actor calls.
    // Null when none were reported.
    public int? CachedInputTokens { get; set; }
    public int? ReasoningTokens { get; set; }

    /// <summary>
    /// Set when this run was created as a resume of an existing run.
    /// The parent run is never modified; it remains in its terminal status.
    /// </summary>
    public Guid? ParentRunId { get; init; }

    /// <summary>
    /// The artifact text of the last completed iteration of the parent run,
    /// injected as seed draft for the first pipeline iteration.
    /// Null for clean-retry resumes and for runs that were not resumed.
    /// </summary>
    public string? SeedDraftText { get; init; }

    /// <summary>
    /// What the person asked to change for this version of the order, as sent to the writer (their words and the sentences of the
    /// quick buttons). <c>null</c> for runs that are no change.
    /// </summary>
    public string? RevisionHint { get; init; }

    /// <summary>
    /// Word count of <see cref="FinalText"/>, computed once when the pipeline completes.
    /// Null when <see cref="FinalText"/> is null/empty.
    /// </summary>
    public int? WordCount { get; set; }

    /// <summary>
    /// What the run delivered. Set once when the run ends; <c>null</c> while it is pending or running.
    /// Independent of <see cref="Status"/>.
    /// </summary>
    public RunOutcome? Outcome { get; init; }

    /// <summary>Number of the iteration whose text was delivered, or <c>null</c> when no text was delivered.</summary>
    public int? DeliveredIteration { get; init; }

    /// <summary>Structured run report as JSON. Reserved for the result report; <c>null</c> until it is written.</summary>
    public string? ReportJson { get; init; }

    /// <summary>Order the run belongs to; every run the application creates has one.</summary>
    public Guid? OrderId { get; init; }

    /// <summary>Version of the order this run produced, 1..n; <c>null</c> when the run has no order.</summary>
    public int? OrderVersion { get; init; }

    /// <summary>Fact-check mode the run was checked with (<c>Off|Shadow|Report|On</c>); <c>null</c> until the run set it.</summary>
    public string? FactCheckMode { get; init; }
}
