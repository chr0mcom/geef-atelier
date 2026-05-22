using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Geef.Atelier.Application.Crew.Grounding;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Grounding.AcademicSearch;

/// <summary>
/// OpenAlex API adapter. Free, very broad coverage. Uses the "polite pool" via a dedicated
/// HTTP client that sets a <c>User-Agent</c> identifying the application.
/// Implements exponential-backoff retry on HTTP 429.
/// </summary>
internal sealed class OpenAlexSource(IHttpClientFactory httpClientFactory, ILogger<OpenAlexSource> logger) : IAcademicSource
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private const string BaseUrl = "https://api.openalex.org/works";
    private const string SelectFields = "title,authorships,abstract_inverted_index,doi,publication_date,primary_location";

    public string SourceName => "openalex";

    public async Task<IReadOnlyList<AcademicPaper>> SearchAsync(string query, AcademicSearchOptions options, CancellationToken ct)
    {
        var url = $"{BaseUrl}?search={Uri.EscapeDataString(query)}&per_page={options.MaxPapers}&select={SelectFields}";

        if (!string.IsNullOrWhiteSpace(options.DateFrom))
            url += $"&filter=publication_year:{NormalizeYear(options.DateFrom)}";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);

            try
            {
                var http = httpClientFactory.CreateClient("academic-openalex");
                using var response = await http.GetAsync(url, ct);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    logger.LogWarning("OpenAlexSource: rate-limited (429), attempt {Attempt}/3.", attempt + 1);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<OpenAlexResponse>(JsonOpts, ct);
                if (result?.Results is not { Count: > 0 } results)
                    return [];

                return results.Select(MapWork).ToList();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "OpenAlexSource: request failed on attempt {Attempt}/3.", attempt + 1);
                if (attempt == 2) return [];
            }
        }

        return [];
    }

    private static string NormalizeYear(string dateFrom)
    {
        if (dateFrom.Length >= 4 && int.TryParse(dateFrom[..4], out var year))
            return year.ToString();
        return dateFrom;
    }

    private static AcademicPaper MapWork(OpenAlexWork w)
    {
        var authors = w.Authorships?
            .Select(a => a.Author?.DisplayName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();
        var authorsStr = authors?.Count > 0 ? string.Join("; ", authors) : null;

        // Reconstruct abstract from inverted index (word → [position, ...])
        var abstract_ = ReconstructAbstract(w.AbstractInvertedIndex);

        var doi = !string.IsNullOrWhiteSpace(w.Doi)
            ? w.Doi.Replace("https://doi.org/", "", StringComparison.OrdinalIgnoreCase)
            : null;

        var url = w.PrimaryLocation?.LandingPageUrl
               ?? (doi is not null ? $"https://doi.org/{doi}" : null);

        DateTimeOffset? published = null;
        if (!string.IsNullOrWhiteSpace(w.PublicationDate) &&
            DateTimeOffset.TryParse(w.PublicationDate, out var dt))
            published = dt;

        return new AcademicPaper(
            Title: w.Title ?? "(untitled)",
            Authors: authorsStr,
            Abstract: abstract_,
            Doi: doi,
            ArxivId: null,
            Url: url,
            PublishedDate: published);
    }

    private static string? ReconstructAbstract(Dictionary<string, List<int>>? invertedIndex)
    {
        if (invertedIndex is null || invertedIndex.Count == 0)
            return null;

        var maxPos = invertedIndex.Values.SelectMany(v => v).DefaultIfEmpty(0).Max();
        var words = new string?[maxPos + 1];
        foreach (var (word, positions) in invertedIndex)
            foreach (var pos in positions)
                if (pos >= 0 && pos < words.Length)
                    words[pos] = word;

        return string.Join(" ", words.Where(w => w is not null));
    }

    // ── DTOs ─────────────────────────────────────────────────────────────────

    private sealed class OpenAlexResponse
    {
        [JsonPropertyName("results")] public List<OpenAlexWork>? Results { get; set; }
    }

    private sealed class OpenAlexWork
    {
        [JsonPropertyName("title")]                   public string? Title { get; set; }
        [JsonPropertyName("doi")]                     public string? Doi { get; set; }
        [JsonPropertyName("publication_date")]        public string? PublicationDate { get; set; }
        [JsonPropertyName("authorships")]             public List<OpenAlexAuthorship>? Authorships { get; set; }
        [JsonPropertyName("abstract_inverted_index")] public Dictionary<string, List<int>>? AbstractInvertedIndex { get; set; }
        [JsonPropertyName("primary_location")]        public OpenAlexPrimaryLocation? PrimaryLocation { get; set; }
    }

    private sealed class OpenAlexAuthorship
    {
        [JsonPropertyName("author")] public OpenAlexAuthor? Author { get; set; }
    }

    private sealed class OpenAlexAuthor
    {
        [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
    }

    private sealed class OpenAlexPrimaryLocation
    {
        [JsonPropertyName("landing_page_url")] public string? LandingPageUrl { get; set; }
    }
}
