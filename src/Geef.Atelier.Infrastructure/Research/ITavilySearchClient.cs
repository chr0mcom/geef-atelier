namespace Geef.Atelier.Infrastructure.Research;

/// <summary>A search result: address, title and the raw page text (<c>null</c> when the search had none).</summary>
internal sealed record TavilyHit(string Url, string Title, string? RawContent);

/// <summary>Results of one search; <see cref="ErrorCode"/> is set when the search failed and then <see cref="Hits"/> is empty.</summary>
internal sealed record TavilySearchOutcome(IReadOnlyList<TavilyHit> Hits, string? ErrorCode, int DurationMs);

/// <summary>One web search with the raw text of the pages found; never repeated, never fetches a page itself.</summary>
internal interface ITavilySearchClient
{
    /// <summary>Searches <paramref name="query"/> once and returns at most <paramref name="maxResults"/> results with raw text; never throws for search failures.</summary>
    Task<TavilySearchOutcome> SearchAsync(string query, int maxResults, CancellationToken ct);
}
