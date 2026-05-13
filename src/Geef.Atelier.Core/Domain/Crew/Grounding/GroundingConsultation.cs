namespace Geef.Atelier.Core.Domain.Crew.Grounding;

/// <summary>
/// Immutable record of a single grounding-provider consultation within a pipeline run.
/// Persisted to the <c>GroundingConsultations</c> table for transparency and cost tracking.
/// </summary>
/// <param name="Id">Unique identifier for this consultation record.</param>
/// <param name="RunId">The run this consultation belongs to.</param>
/// <param name="GroundingProviderName">Name of the <see cref="GroundingProviderProfile"/> used.</param>
/// <param name="Query">The query sent to the grounding provider (typically the full briefing text).</param>
/// <param name="Citations">Sources returned by the provider.</param>
/// <param name="TokensOrCreditsUsed">Provider-specific unit; credits for Tavily, tokens for vector stores.</param>
/// <param name="CostEur">Estimated cost in EUR, or <c>null</c> when not calculable.</param>
/// <param name="CreatedAt">UTC timestamp when the consultation was recorded.</param>
public sealed record GroundingConsultation(
    Guid Id,
    Guid RunId,
    string GroundingProviderName,
    string Query,
    IReadOnlyList<SourceCitation> Citations,
    int TokensOrCreditsUsed,
    decimal? CostEur,
    DateTimeOffset CreatedAt);
