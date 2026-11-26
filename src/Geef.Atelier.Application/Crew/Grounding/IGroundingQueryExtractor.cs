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
    /// Implementations fail closed and never throw: on any failure, and when no query comes back, they return
    /// <see cref="GroundingQuery.NotFormed"/>, so nothing is searched. The briefing itself is never the query.
    /// </summary>
    Task<GroundingQuery> ExtractAsync(string briefingText, CancellationToken ct);
}

/// <summary>Outcome of <see cref="IGroundingQueryExtractor.ExtractAsync"/>.</summary>
/// <param name="ShouldSearch">
/// <c>false</c> when the briefing does not benefit from current web information,
/// in which case the provider should skip the external call entirely.
/// </param>
/// <param name="Query">The focused search query. Empty when <see cref="ShouldSearch"/> is <c>false</c>.</param>
/// <param name="Failed">
/// <c>true</c> when no query could be formed (the call failed or returned nothing); then <see cref="ShouldSearch"/> is <c>false</c>
/// as well, and the provider reports the source as skipped instead of searching.
/// </param>
public sealed record GroundingQuery(bool ShouldSearch, string Query, bool Failed = false)
{
    /// <summary>The outcome when no query could be formed: nothing is searched.</summary>
    public static GroundingQuery NotFormed { get; } = new(ShouldSearch: false, Query: string.Empty, Failed: true);
}
