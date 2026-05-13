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
    HttpClient httpClient,
    IOptions<TavilyOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<TavilyGroundingProvider> logger) : IGroundingProvider
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

        var searchDepth = string.Equals(tier, "advanced", StringComparison.OrdinalIgnoreCase) ? "advanced" : "basic";
        var maxResults = int.TryParse(maxResultsStr, out var mr) ? mr : 5;
        var includeAnswer = !string.Equals(includeAnswerStr, "false", StringComparison.OrdinalIgnoreCase);

        var requestBody = new TavilySearchRequest(
            ApiKey: opts.ApiKey,
            Query: briefingText,
            SearchDepth: searchDepth,
            IncludeAnswer: includeAnswer,
            MaxResults: maxResults);

        logger.LogInformation("Tavily grounding: run={RunId} provider={Profile} depth={Depth} maxResults={Max}",
            runId, profile.Name, searchDepth, maxResults);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(opts.RequestTimeoutSeconds));

        var response = await httpClient.PostAsJsonAsync("/search", requestBody, JsonOpts, cts.Token);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<TavilySearchResponse>(JsonOpts, cts.Token)
            ?? throw new InvalidOperationException("Tavily returned an empty response body.");

        var citations = result.Results
            .Select(r => new SourceCitation(
                Title: r.Title ?? string.Empty,
                Url: r.Url,
                Snippet: Truncate(r.Content ?? string.Empty, 300),
                DocumentReference: null,
                RelevanceScore: r.Score))
            .ToList();

        var enrichedContext = BuildEnrichedContext(result.Answer, citations);

        var costUsd = string.Equals(searchDepth, "advanced", StringComparison.OrdinalIgnoreCase)
            ? opts.AdvancedSearchCostUsd
            : opts.BasicSearchCostUsd;
        var costEur = (decimal)(costUsd * opts.UsdToEurRate);

        var consultation = new GroundingConsultation(
            Id: Guid.NewGuid(),
            RunId: runId,
            GroundingProviderName: profile.Name,
            Query: briefingText,
            Citations: citations,
            TokensOrCreditsUsed: 1,
            CostEur: costEur,
            CreatedAt: DateTimeOffset.UtcNow);

        await using var scope = scopeFactory.CreateAsyncScope();
        var consultationRepository = scope.ServiceProvider.GetRequiredService<IGroundingConsultationRepository>();
        await consultationRepository.CreateAsync(consultation, ct);

        return new GroundingResult(
            ProviderName: profile.Name,
            EnrichedContext: enrichedContext,
            Citations: citations,
            TokensOrCreditsUsed: 1,
            CostEur: costEur);
    }

    private static string BuildEnrichedContext(string? answer, IReadOnlyList<SourceCitation> citations)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[Web research context]");
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
