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
/// <see cref="ResearchAddress.MaxChars"/>) are left out. A title is cut to <see cref="MaxTitleChars"/>; an answer larger than
/// <see cref="MaxAnswerBytes"/> is not read and ends as <see cref="InvalidAnswerCode"/>.
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

    /// <summary>Longest title of a page that is kept.</summary>
    public const int MaxTitleChars = 300;

    /// <summary>Largest answer of the search service that is read.</summary>
    public const int MaxAnswerBytes = 4 * 1024 * 1024;

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

            var json = await ReadLimitedAsync(response.Content, linked.Token);
            if (json is null)
                return Failed(InvalidAnswerCode, started);
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
                hits.Add(new TavilyHit(address, (result.Title ?? string.Empty).Cut(MaxTitleChars), raw));
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

    private static async Task<string?> ReadLimitedAsync(HttpContent content, CancellationToken ct)
    {
        if (content.Headers.ContentLength > MaxAnswerBytes)
            return null;
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxAnswerBytes)
                return null;
            buffer.Write(chunk, 0, read);
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length).TrimStart('\uFEFF');
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
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "ca", "bzw", "vgl", "ggf", "evtl", "inkl", "exkl", "zzgl", "abzgl", "Nr", "Dr", "Prof", "Hr", "Fr", "St", "Str", "Abs", "Art",
        "Mio", "Mrd", "Tsd", "max", "min", "Mr", "Mrs", "Ms", "vs", "approx", "No", "Jr", "Sr", "Inc", "Ltd", "Co", "Bd", "Jh", "Tel",
    };

    /// <summary>
    /// <paramref name="text"/> when it has at most <paramref name="maxChars"/> characters. A longer text is cut back to the last safe end
    /// within the first <paramref name="maxChars"/> characters that keeps at least half of them: a line break before a line that does
    /// not go on in lower case, a <c>.</c>, <c>!</c> or <c>?</c> followed by white space, or <c>。</c>, <c>！</c> or <c>？</c>. A dot
    /// right after a digit (<c>1.250</c>, <c>1. Mai</c>), after a single letter or a known abbreviation (<c>ca.</c>, <c>z.B.</c>), or
    /// before a word in lower case or a digit never counts. Without such an end the cut falls on the last white space that does not
    /// stand between digits, then after the last comma, semicolon, colon or closing bracket (also the full-width ones of Chinese and
    /// Japanese) that does not stand between digits, and only without any of these is the text empty; a cut never ends inside a word
    /// or a number.
    /// </summary>
    public static string Cut(this string text, int maxChars)
    {
        if (maxChars <= 0)
            return string.Empty;
        if (text.Length <= maxChars)
            return text;

        var half = (maxChars + 1) / 2;
        for (var i = maxChars - 1; i >= half; i--)
        {
            if (text[i] is '\n' or '\r' && text.LineEndsAt(i))
                return text[..i].TrimEnd();
            if (text.EndsSentenceAt(i))
                return text[..(i + 1)];
        }
        for (var i = maxChars; i > 0; i--)
        {
            if (char.IsWhiteSpace(text[i]) && !(char.IsDigit(text[i - 1]) && i + 1 < text.Length && char.IsDigit(text[i + 1])))
                return text[..i].TrimEnd();
        }
        for (var i = maxChars - 1; i > 0; i--)
        {
            if (text[i] is '\u3001' or '\uFF0C' or '\uFF1B' or '\uFF1A' or '\u300D' or '\u300F' or '\uFF09'
                || text[i] is ',' or ';' or ':' or ')' && !(char.IsDigit(text[i - 1]) && char.IsDigit(text[i + 1])))
                return text[..(i + 1)];
        }
        return string.Empty;
    }

    private static bool LineEndsAt(this string text, int index)
    {
        var next = index + 1;
        while (next < text.Length && char.IsWhiteSpace(text[next]))
            next++;
        return next >= text.Length || !char.IsLower(text[next]);
    }

    private static bool EndsSentenceAt(this string text, int index)
    {
        if (text[index] is '\u3002' or '\uFF01' or '\uFF1F')
            return true;
        if (text[index] is not ('.' or '!' or '?') || index + 1 >= text.Length || !char.IsWhiteSpace(text[index + 1]))
            return false;
        if (text[index] != '.')
            return true;
        var next = index + 1;
        while (next < text.Length && char.IsWhiteSpace(text[next]))
            next++;
        if (next < text.Length && (char.IsLower(text[next]) || char.IsDigit(text[next])))
            return false;
        var start = index;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '.'))
            start--;
        var word = text[start..index];
        return word.Length == 0 || (!char.IsDigit(word[^1]) && word.Count(char.IsLetter) > 1 && !word.Contains('.') && !Abbreviations.Contains(word));
    }
}
