namespace Geef.Atelier.Application.Crew.Grounding;

/// <summary>
/// Refines a raw briefing into a single focused web-search query, or signals that
/// web search would not benefit the briefing (pure reasoning, mathematics, opinion,
/// or creative tasks). Lives in Application so grounding providers can depend on it
/// without referencing the LLM Infrastructure directly.
/// </summary>
public interface IGroundingQueryExtractor
{
    /// <summary>
    /// Produces a focused search query for <paramref name="briefingText"/>.
    /// Implementations must degrade gracefully: on any failure they return
    /// <see cref="GroundingQuery.ShouldSearch"/> <c>= true</c> with the raw briefing
    /// as the query, never throwing.
    /// </summary>
    Task<GroundingQuery> ExtractAsync(string briefingText, CancellationToken ct);
}

/// <summary>Outcome of <see cref="IGroundingQueryExtractor.ExtractAsync"/>.</summary>
/// <param name="ShouldSearch">
/// <c>false</c> when the briefing does not benefit from current web information,
/// in which case the provider should skip the external call entirely.
/// </param>
/// <param name="Query">The focused search query. Empty when <see cref="ShouldSearch"/> is <c>false</c>.</param>
public sealed record GroundingQuery(bool ShouldSearch, string Query);
