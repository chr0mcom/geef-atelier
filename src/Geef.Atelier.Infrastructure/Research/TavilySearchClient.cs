using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Infrastructure.Grounding;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Research;

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

    /// <summary>Error code when no Tavily key is configured; nothing is sent.</summary>
    public const string NoKeyCode = "no_key";

    /// <summary>Error code of a search that ran into <see cref="ResearchOptions.SearchTimeout"/>.</summary>
    public const string TimeoutCode = "timeout";

    /// <summary>Error code of a search without an answer (connection failed).</summary>
    public const string HttpCode = "http";

    /// <summary>Error code of an answer that is not the expected JSON.</summary>
    public const string InvalidAnswerCode = "invalid_answer";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<TavilySearchOutcome> SearchAsync(string query, int maxResults, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var key = tavily.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(key))
            return Failed(NoKeyCode, started);

        var settings = options.Value;
        using var timeout = new CancellationTokenSource(settings.SearchTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            var body = JsonSerializer.Serialize(new SearchRequest(key, query, SearchDepth, Math.Max(1, maxResults), true, false), JsonOptions);
            using var request = new HttpRequestMessage(HttpMethod.Post, "search")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, linked.Token);
            if (!response.IsSuccessStatusCode)
                return Failed($"http_{(int)response.StatusCode}", started);

            var json = await response.Content.ReadAsStringAsync(linked.Token);
            var answer = Parse(json);
            if (answer is null)
                return Failed(InvalidAnswerCode, started);

            var hits = new List<TavilyHit>();
            var skipped = 0;
            foreach (var result in answer.Results ?? [])
            {
                if (hits.Count >= Math.Max(1, maxResults))
                    break;
                if (result is null || !IsWebAddress(result.Url) || string.IsNullOrWhiteSpace(result.RawContent))
                {
                    skipped++;
                    continue;
                }
                hits.Add(new TavilyHit(result.Url!.Trim(), result.Title ?? string.Empty, result.RawContent.Cut(settings.RawCharsPerPage)));
            }

            return new TavilySearchOutcome(hits, null, Elapsed(started)) { Skipped = skipped };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Failed(TimeoutCode, started);
        }
        catch (HttpRequestException)
        {
            return Failed(HttpCode, started);
        }
        catch (JsonException)
        {
            return Failed(InvalidAnswerCode, started);
        }
    }

    /// <summary>True for an absolute <c>http</c> or <c>https</c> address.</summary>
    internal static bool IsWebAddress(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrEmpty(uri.Host);

    private static SearchResponse? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.Object
            ? document.RootElement.Deserialize<SearchResponse>(JsonOptions)
            : null;
    }

    private TavilySearchOutcome Failed(string code, long started) => new([], code, Elapsed(started));

    private int Elapsed(long started) => (int)Math.Min(int.MaxValue, time.GetElapsedTime(started).TotalMilliseconds);

    private sealed record SearchRequest(
        [property: JsonPropertyName("api_key")] string ApiKey,
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("search_depth")] string SearchDepth,
        [property: JsonPropertyName("max_results")] int MaxResults,
        [property: JsonPropertyName("include_raw_content")] bool IncludeRawContent,
        [property: JsonPropertyName("include_answer")] bool IncludeAnswer);

    private sealed class SearchResponse
    {
        [JsonPropertyName("results")]
        public List<SearchResult?>? Results { get; set; }
    }

    private sealed class SearchResult
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("raw_content")]
        public string? RawContent { get; set; }
    }
}

/// <summary>Text helpers of the web search.</summary>
internal static class TavilyTextExtensions
{
    /// <summary>The first <paramref name="maxChars"/> characters of <paramref name="text"/>, never ending inside a surrogate pair.</summary>
    public static string Cut(this string text, int maxChars)
    {
        if (maxChars <= 0)
            return string.Empty;
        if (text.Length <= maxChars)
            return text;
        var cut = maxChars;
        if (char.IsHighSurrogate(text[cut - 1]))
            cut--;
        return text[..cut];
    }
}
