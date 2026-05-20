using System.Globalization;
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

internal sealed class TavilyGroundingProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<TavilyOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<TavilyGroundingProvider> logger,
    IGroundingQueryExtractor? queryExtractor = null) : IGroundingProvider
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public string ProviderType => "tavily";

    public async Task<GroundingResult> EnrichAsync(
        string briefingText,
        GroundingProviderProfile profile,
        Guid runId,
        CancellationToken ct)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.ApiKey))
            throw new InvalidOperationException(
                "TAVILY_API_KEY is not configured. The grounding provider 'tavily' cannot be used until the key is set in the environment.");

        profile.ProviderSettings.TryGetValue("Tier", out var tier);
        profile.ProviderSettings.TryGetValue("MaxResults", out var maxResultsStr);
        profile.ProviderSettings.TryGetValue("IncludeAnswer", out var includeAnswerStr);
        profile.ProviderSettings.TryGetValue("MinRelevanceScore", out var minScoreStr);
        profile.ProviderSettings.TryGetValue("ExtractQuery", out var extractQueryStr);

        var searchDepth = string.Equals(tier, "advanced", StringComparison.OrdinalIgnoreCase) ? "advanced" : "basic";
        var maxResults = int.TryParse(maxResultsStr, out var mr) ? mr : 5;
        var includeAnswer = !string.Equals(includeAnswerStr, "false", StringComparison.OrdinalIgnoreCase);
        var minRelevanceScore = double.TryParse(minScoreStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var msc)
            ? msc
            : opts.DefaultMinRelevanceScore;
        var extractQuery = !string.Equals(extractQueryStr, "false", StringComparison.OrdinalIgnoreCase);

        var costUsd = string.Equals(searchDepth, "advanced", StringComparison.OrdinalIgnoreCase)
            ? opts.AdvancedSearchCostUsd
            : opts.BasicSearchCostUsd;
        var costEur = (decimal)(costUsd * opts.UsdToEurRate);

        // Refine the briefing into a focused query — or skip web search entirely
        // when the briefing does not benefit from current web information.
        var query = briefingText;
        if (extractQuery && queryExtractor is not null)
        {
            var extracted = await queryExtractor.ExtractAsync(briefingText, ct);
            if (!extracted.ShouldSearch)
            {
                logger.LogInformation(
                    "Tavily grounding: run={RunId} provider={Profile} skipped (briefing not search-worthy).",
                    runId, profile.Name);
                var skippedId = await PersistConsultationAsync(runId, profile.Name, briefingText, [], 0, null, ct);
                return new GroundingResult(profile.Name, string.Empty, [], 0, null, skippedId);
            }
            query = extracted.Query;
        }

        var requestBody = new TavilySearchRequest(
            ApiKey: opts.ApiKey,
            Query: query,
            SearchDepth: searchDepth,
            IncludeAnswer: includeAnswer,
            MaxResults: maxResults);

        logger.LogInformation(
            "Tavily grounding: run={RunId} provider={Profile} depth={Depth} maxResults={Max} minScore={MinScore}",
            runId, profile.Name, searchDepth, maxResults, minRelevanceScore);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(opts.RequestTimeoutSeconds));

        // Not disposed: IHttpClientFactory owns the lifetime of clients it hands out.
        var httpClient = httpClientFactory.CreateClient("tavily");
        var response = await httpClient.PostAsJsonAsync("search", requestBody, JsonOpts, cts.Token);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<TavilySearchResponse>(JsonOpts, cts.Token)
            ?? throw new InvalidOperationException("Tavily returned an empty response body.");

        var citations = result.Results
            .Where(r => r.Score is null || r.Score >= minRelevanceScore)
            .Select(r => new SourceCitation(
                Title: r.Title ?? string.Empty,
                Url: r.Url,
                Snippet: Truncate(r.Content ?? string.Empty, 300),
                DocumentReference: null,
                RelevanceScore: r.Score))
            .ToList();

        var droppedCount = result.Results.Count - citations.Count;
        if (droppedCount > 0)
            logger.LogInformation(
                "Tavily grounding: run={RunId} dropped {Dropped}/{Total} results below minScore={MinScore}",
                runId, droppedCount, result.Results.Count, minRelevanceScore);

        // When nothing clears the relevance bar, the synthesised answer was derived
        // from the rejected sources too — drop it and inject no web context at all.
        var enrichedContext = citations.Count == 0
            ? string.Empty
            : BuildEnrichedContext(result.Answer, citations);

        var consultationId = await PersistConsultationAsync(runId, profile.Name, query, citations, 1, costEur, ct);

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
        sb.AppendLine("[Web research context]");
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
                sb.AppendLine($"{i + 1}. {c.Title} ({c.Url})");
                sb.AppendLine($"   {c.Snippet}");
            }
        }
        sb.Append("[End of web research context]");
        return sb.ToString();
    }

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength] + "…";

    // ---- Tavily request/response DTOs ----

    private sealed record TavilySearchRequest(
        [property: JsonPropertyName("api_key")]    string ApiKey,
        [property: JsonPropertyName("query")]      string Query,
        [property: JsonPropertyName("search_depth")] string SearchDepth,
        [property: JsonPropertyName("include_answer")] bool IncludeAnswer,
        [property: JsonPropertyName("max_results")] int MaxResults);

    private sealed class TavilySearchResponse
    {
        [JsonPropertyName("answer")]
        public string? Answer { get; set; }

        [JsonPropertyName("results")]
        public List<TavilyResult> Results { get; set; } = [];
    }

    private sealed class TavilyResult
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("score")]
        public double? Score { get; set; }
    }
}
