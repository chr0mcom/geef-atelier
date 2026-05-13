namespace Geef.Atelier.Core.Domain;

/// <summary>Represents a single text-generation run submitted to the Geef pipeline.</summary>
public sealed record RunEntity
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
}
