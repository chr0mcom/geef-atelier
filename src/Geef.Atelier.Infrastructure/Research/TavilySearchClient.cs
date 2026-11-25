using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Research;
using Geef.Atelier.Infrastructure.Grounding;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Research;

/// <summary>
/// One <c>POST search</c> per topic over the named client <see cref="HttpClientName"/> (no retry handler): <c>search_depth</c> basic,
/// raw page text on, Tavily's own answer off. Uses only <c>url</c>, <c>title</c> and <c>raw_content</c> of a result, cut to
/// <see cref="ResearchOptions.RawCharsPerPage"/> (<see cref="TavilyTextExtensions.Cut"/>); results without raw text and results whose
/// address is not acceptable by <see cref="ResearchAddress.Acceptable"/> (no http or https address, user information, longer than
/// <see cref="ResearchAddress.MaxChars"/>) are left out.
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
                var address = ResearchAddress.Acceptable(result?.Url);
                var raw = string.IsNullOrWhiteSpace(result?.RawContent) ? string.Empty : result.RawContent.Cut(settings.RawCharsPerPage);
                if (result is null || address is null || string.IsNullOrWhiteSpace(raw))
                {
                    skipped++;
                    continue;
                }
                hits.Add(new TavilyHit(address, result.Title ?? string.Empty, raw));
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

    /// <summary>True for an address the research keeps (<see cref="ResearchAddress.Acceptable"/>).</summary>
    internal static bool IsWebAddress(string? url) => ResearchAddress.Acceptable(url) is not null;

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
    /// <summary>
    /// <paramref name="text"/> when it has at most <paramref name="maxChars"/> characters. A longer text is cut back to the last safe end
    /// within the first <paramref name="maxChars"/> characters: a line break, or a <c>.</c>, <c>!</c> or <c>?</c> followed by white
    /// space, where a dot right after a digit never counts (a number such as <c>1.250</c> or a date such as <c>1. Mai</c>). Without
    /// such an end the cut falls on the last white space that does not stand between digits, and without that the text is empty; a
    /// cut never ends inside a word or a number.
    /// </summary>
    public static string Cut(this string text, int maxChars)
    {
        if (maxChars <= 0)
            return string.Empty;
        if (text.Length <= maxChars)
            return text;

        for (var i = maxChars - 1; i >= 0; i--)
        {
            if (text[i] is '\n' or '\r')
                return text[..i].TrimEnd();
            if (text.EndsSentenceAt(i))
                return text[..(i + 1)];
        }
        for (var i = maxChars; i > 0; i--)
        {
            if (char.IsWhiteSpace(text[i]) && !(char.IsDigit(text[i - 1]) && i + 1 < text.Length && char.IsDigit(text[i + 1])))
                return text[..i].TrimEnd();
        }
        return string.Empty;
    }

    private static bool EndsSentenceAt(this string text, int index) =>
        text[index] is '.' or '!' or '?'
        && index + 1 < text.Length
        && char.IsWhiteSpace(text[index + 1])
        && !(text[index] == '.' && index > 0 && char.IsDigit(text[index - 1]));
}
