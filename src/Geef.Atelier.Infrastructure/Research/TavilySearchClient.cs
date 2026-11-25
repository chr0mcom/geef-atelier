using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Infrastructure.Grounding;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Research;

#pragma warning disable CS9113

/// <summary>
/// One <c>POST search</c> per topic over the named client <see cref="HttpClientName"/> (no retry handler): <c>search_depth</c> basic,
/// raw page text on, Tavily's own answer off. Uses only <c>url</c>, <c>title</c> and <c>raw_content</c> of a result, cut to
/// <see cref="ResearchOptions.RawCharsPerPage"/>; results without raw text or without an http or https address are left out.
/// </summary>
internal sealed class TavilySearchClient(
    IHttpClientFactory httpClientFactory,
    IOptions<TavilyOptions> tavily,
    IOptions<ResearchOptions> options,
    TimeProvider time) : ITavilySearchClient
{
    public const string HttpClientName = "tavily";
    public const string SearchDepth = "basic";

    public Task<TavilySearchOutcome> SearchAsync(string query, int maxResults, CancellationToken ct) => throw new NotImplementedException();
}
