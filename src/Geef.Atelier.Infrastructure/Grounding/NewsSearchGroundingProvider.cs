using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Persistence.Crew;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Grounding;

internal sealed class NewsSearchGroundingProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<TavilyOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<NewsSearchGroundingProvider> logger) : IGroundingProvider
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public string ProviderType => GroundingProviderTypes.NewsSearch;

    public async Task<GroundingResult> EnrichAsync(
        string briefingText,
        GroundingProviderProfile profile,
        Guid runId,
        CancellationToken ct)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.ApiKey))
            throw new InvalidOperationException(
                "TAVILY_API_KEY is not configured. The grounding provider 'news-search' cannot be used until the key is set in the environment.");

        var maxResults = profile.NewsMaxResults;
        var searchDepth = profile.NewsSearchDepth ?? "basic";
        var days = profile.RecencyDays;

        var costUsd = string.Equals(searchDepth, "advanced", StringComparison.OrdinalIgnoreCase)
            ? opts.AdvancedSearchCostUsd
            : opts.BasicSearchCostUsd;
        var costEur = (decimal)(costUsd * opts.UsdToEurRate);

        logger.LogInformation(
            "NewsSearch grounding: run={RunId} provider={Profile} depth={Depth} maxResults={Max} days={Days}",
            runId, profile.Name, searchDepth, maxResults, days);

        var requestBody = new TavilyNewsSearchRequest(
            ApiKey: opts.ApiKey,
            Query: briefingText,
            SearchDepth: searchDepth,
            IncludeAnswer: true,
            MaxResults: maxResults,
            Topic: "news",
            Days: days);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(opts.RequestTimeoutSeconds));

        var httpClient = httpClientFactory.CreateClient("tavily");
        var response = await httpClient.PostAsJsonAsync("search", requestBody, JsonOpts, cts.Token);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<TavilyNewsSearchResponse>(JsonOpts, cts.Token)
            ?? throw new InvalidOperationException("Tavily returned an empty response body.");

        var citations = result.Results
            .Select(r => new SourceCitation(
                Title: r.Title ?? string.Empty,
                Url: r.Url,
                Snippet: Truncate(r.Content ?? string.Empty, 300),
                DocumentReference: null,
                RelevanceScore: r.Score,
                PublishedDate: r.PublishedDate))
            .ToList();

        var enrichedContext = citations.Count == 0
            ? string.Empty
            : BuildEnrichedContext(result.Answer, citations);

        var consultationId = await PersistConsultationAsync(runId, profile.Name, briefingText, citations, 1, costEur, ct);

        return new GroundingResult(
            ProviderName: profile.Name,
            EnrichedContext: enrichedContext,
            Citations: citations,
            TokensOrCreditsUsed: 1,
            CostEur: costEur,
            ConsultationId: consultationId);
    }

    private async Task<Guid> PersistConsultationAsync(
        Guid runId,
        string providerName,
        string query,
        IReadOnlyList<SourceCitation> citations,
        int tokensOrCredits,
        decimal? costEur,
        CancellationToken ct)
    {
        var consultation = new GroundingConsultation(
            Id:                    Guid.NewGuid(),
            RunId:                 runId,
            GroundingProviderName: providerName,
            Query:                 query,
            Citations:             citations,
            TokensOrCreditsUsed:   tokensOrCredits,
            CostEur:               costEur,
            CreatedAt:             DateTimeOffset.UtcNow);

        await using var scope = scopeFactory.CreateAsyncScope();
        var consultationRepository = scope.ServiceProvider.GetRequiredService<IGroundingConsultationRepository>();
        await consultationRepository.CreateAsync(consultation, ct);
        return consultation.Id;
    }

    private static string BuildEnrichedContext(string? answer, IReadOnlyList<SourceCitation> citations)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[News research context]");
        sb.AppendLine("Use the material below only where it is clearly relevant to the briefing. "
            + "Ignore anything off-topic and never restate that this context was provided.");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(answer))
        {
            sb.AppendLine("Tavily synthesized answer:");
            sb.AppendLine(answer);
            sb.AppendLine();
        }
        if (citations.Count > 0)
        {
            sb.AppendLine("Sources:");
            for (var i = 0; i < citations.Count; i++)
            {
                var c = citations[i];
                var date = c.PublishedDate.HasValue ? $" [{c.PublishedDate.Value:yyyy-MM-dd}]" : string.Empty;
                sb.AppendLine($"{i + 1}. {c.Title}{date} ({c.Url})");
                sb.AppendLine($"   {c.Snippet}");
            }
        }
        sb.Append("[End of news research context]");
        return sb.ToString();
    }

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength] + "…";

    // ---- Tavily news request/response DTOs ----

    private sealed record TavilyNewsSearchRequest(
        [property: JsonPropertyName("api_key")]      string ApiKey,
        [property: JsonPropertyName("query")]        string Query,
        [property: JsonPropertyName("search_depth")] string SearchDepth,
        [property: JsonPropertyName("include_answer")] bool IncludeAnswer,
        [property: JsonPropertyName("max_results")]  int MaxResults,
        [property: JsonPropertyName("topic")]        string Topic,
        [property: JsonPropertyName("days")]         int Days);

    private sealed class TavilyNewsSearchResponse
    {
        [JsonPropertyName("answer")]
        public string? Answer { get; set; }

        [JsonPropertyName("results")]
        public List<TavilyNewsResult> Results { get; set; } = [];
    }

    private sealed class TavilyNewsResult
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("score")]
        public double? Score { get; set; }

        [JsonPropertyName("published_date")]
        public DateTimeOffset? PublishedDate { get; set; }
    }
}
