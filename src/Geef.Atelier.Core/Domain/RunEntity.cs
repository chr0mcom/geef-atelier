namespace Geef.Atelier.Core.Domain;

/// <summary>Represents a single text-generation run submitted to the Geef pipeline.</summary>
public sealed class RunEntity
{
    public required Guid Id { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required RunStatus Status { get; init; }
    public required string BriefingText { get; init; }

    /// <summary>JSON snapshot of model/crew/budget config at submission time.</summary>
    public required string ConfigJson { get; init; }

    public string? CreatedByUser { get; init; }

    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? FinalText { get; init; }
    public string? ErrorMessage { get; init; }
    public int TokensTotal { get; init; }
    public decimal CostTotal { get; init; }

    /// <summary>Set to true when a cancellation request has been submitted for this run.</summary>
    public bool CancellationRequested { get; init; }

    /// <summary>
    /// Name of the crew template the run was submitted with, or null when an inline custom crew was used.
    /// Historical runs (pre-PS-5) are migrated to <c>"klassik"</c>.
    /// </summary>
    public string? CrewTemplateName { get; init; }

    /// <summary>
    /// JSONB-serialised <see cref="Crew.CrewSnapshot"/> capturing the fully-dereferenced crew
    /// configuration used for this run. Persisted to keep runs reproducible after profiles change.
    /// </summary>
    public string? CrewSnapshot { get; init; }

    /// <summary>
    /// Set to true when an advisor-driven convergence-failure recovery pass has been attempted for this run.
    /// Prevents infinite retry loops: only one advisor retry is permitted per run.
    /// </summary>
    public bool AdvisorRetryAttempted { get; init; }

    // Cost tracking — null for runs before Step16 migration or when CostTracking:Enabled=false.
    public decimal? TotalCostEur { get; set; }
    public decimal? LlmCostEur { get; set; }
    public decimal? GroundingCostEur { get; set; }

    // Finalizer tracking — null when no finalizers ran or when the run predates Step22.
    public decimal? FinalizerCostEur { get; set; }

    /// <summary>
    /// Set when one or more finalizer steps fail. The run status remains <c>Completed</c>;
    /// this field captures the partial-failure description for display in the UI.
    /// </summary>
    public string? FinalizerErrorMessage { get; set; }

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
    /// Word count of <see cref="FinalText"/>, computed once when the pipeline completes.
    /// Null for runs before Step25 migration or when <see cref="FinalText"/> is null/empty.
    /// </summary>
    public int? WordCount { get; set; }
}
