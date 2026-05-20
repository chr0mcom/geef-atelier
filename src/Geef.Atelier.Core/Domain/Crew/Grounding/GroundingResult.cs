namespace Geef.Atelier.Core.Domain.Crew.Grounding;

/// <summary>
/// The output produced by a single <c>IGroundingProvider</c> call.
/// <see cref="EnrichedContext"/> is prepended to the executor prompt by <c>ProfileBasedExecutor</c>.
/// </summary>
/// <param name="ProviderName">Name of the profile that produced this result.</param>
/// <param name="EnrichedContext">
/// Formatted text block inserted before the executor prompt.
/// For Tavily: a synthesised answer plus numbered source citations.
/// </param>
/// <param name="Citations">Structured citation data for display in the UI.</param>
/// <param name="TokensOrCreditsUsed">Provider-specific cost unit (credits for Tavily).</param>
/// <param name="CostEur">Estimated cost in EUR, or <c>null</c> when not calculable.</param>
/// <param name="ConsultationId">The persisted <see cref="GroundingConsultation"/> id, set after the consultation is recorded.</param>
public sealed record GroundingResult(
    string ProviderName,
    string EnrichedContext,
    IReadOnlyList<SourceCitation> Citations,
    int TokensOrCreditsUsed,
    decimal? CostEur,
    Guid? ConsultationId = null);
