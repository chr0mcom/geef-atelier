using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Geef.Atelier.Application.Crew.Grounding;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Grounding.AcademicSearch;

/// <summary>
/// Semantic Scholar Graph API adapter. Free; optional API key via <see cref="AcademicSearchOptions.ApiKey"/>
/// for higher rate-limits. JSON responses with broad coverage and citation data.
/// Implements exponential-backoff retry on HTTP 429.
/// </summary>
internal sealed class SemanticScholarSource(IHttpClientFactory httpClientFactory, ILogger<SemanticScholarSource> logger) : IAcademicSource
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private const string BaseUrl = "https://api.semanticscholar.org/graph/v1/paper/search";
    private const string Fields = "title,authors,abstract,externalIds,year,publicationDate,url";

    public string SourceName => "semantic-scholar";

    public async Task<IReadOnlyList<AcademicPaper>> SearchAsync(string query, AcademicSearchOptions options, CancellationToken ct)
    {
        var url = $"{BaseUrl}?query={Uri.EscapeDataString(query)}&limit={options.MaxPapers}&fields={Fields}";
        if (!string.IsNullOrWhiteSpace(options.DateFrom))
            url += $"&publicationDateOrYear={Uri.EscapeDataString(options.DateFrom + ":")}";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);

            try
            {
                var http = httpClientFactory.CreateClient("academic-semantic-scholar");
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrWhiteSpace(options.ApiKey))
                    request.Headers.TryAddWithoutValidation("x-api-key", options.ApiKey);

                using var response = await http.SendAsync(request, ct);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    logger.LogWarning("SemanticScholarSource: rate-limited (429), attempt {Attempt}/3.", attempt + 1);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<SemanticScholarResponse>(JsonOpts, ct);
                if (result?.Data is not { Count: > 0 } data)
                    return [];

                return data.Select(MapPaper).ToList();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "SemanticScholarSource: request failed on attempt {Attempt}/3.", attempt + 1);
                if (attempt == 2) return [];
            }
        }

        return [];
    }

    private static AcademicPaper MapPaper(SemanticScholarPaper p)
    {
        var authors = p.Authors?.Select(a => a.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        var authorsStr = authors?.Count > 0 ? string.Join("; ", authors) : null;

        var doi = p.ExternalIds?.TryGetValue("DOI", out var d) == true ? d : null;
        var arxivId = p.ExternalIds?.TryGetValue("ArXiv", out var a) == true ? a : null;

        DateTimeOffset? published = null;
        if (!string.IsNullOrWhiteSpace(p.PublicationDate) &&
            DateTimeOffset.TryParse(p.PublicationDate, out var dt))
            published = dt;
        else if (p.Year.HasValue)
            published = new DateTimeOffset(p.Year.Value, 1, 1, 0, 0, 0, TimeSpan.Zero);

        return new AcademicPaper(
            Title: p.Title ?? "(untitled)",
            Authors: authorsStr,
            Abstract: p.Abstract,
            Doi: doi,
            ArxivId: arxivId,
            Url: p.Url ?? (arxivId is not null ? $"https://arxiv.org/abs/{arxivId}" : null),
            PublishedDate: published);
    }

    // ── DTOs ─────────────────────────────────────────────────────────────────

    private sealed class SemanticScholarResponse
    {
        [JsonPropertyName("data")]
        public List<SemanticScholarPaper>? Data { get; set; }
    }

    private sealed class SemanticScholarPaper
    {
        [JsonPropertyName("title")]       public string? Title          { get; set; }
        [JsonPropertyName("abstract")]    public string? Abstract       { get; set; }
        [JsonPropertyName("authors")]     public List<SsAuthor>? Authors { get; set; }
        [JsonPropertyName("externalIds")] public Dictionary<string, string>? ExternalIds { get; set; }
        [JsonPropertyName("year")]        public int? Year              { get; set; }
        [JsonPropertyName("publicationDate")] public string? PublicationDate { get; set; }
        [JsonPropertyName("url")]         public string? Url            { get; set; }
    }

    private sealed class SsAuthor
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
    }
}
