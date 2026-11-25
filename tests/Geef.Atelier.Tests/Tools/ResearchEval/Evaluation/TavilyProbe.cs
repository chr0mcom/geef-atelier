using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Geef.Atelier.Tools.FactCheckEval;

namespace Geef.Atelier.Tools.ResearchEval;

/// <summary>
/// One request of the Tavily probe VT: how the key was sent, HTTP status, duration, how many results had a non-empty
/// <c>raw_content</c> and how long it was, and every field of the answer except contents (result texts, the synthesized answer, images).
/// </summary>
public sealed record TavilyProbeLine(
    [property: JsonPropertyName("order")] string Order,
    [property: JsonPropertyName("topic")] string Topic,
    [property: JsonPropertyName("auth")] string Auth,
    [property: JsonPropertyName("http_status")] int? HttpStatus,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("duration_ms")] int DurationMs,
    [property: JsonPropertyName("results")] int Results,
    [property: JsonPropertyName("results_with_raw")] int ResultsWithRaw,
    [property: JsonPropertyName("raw_lengths")] IReadOnlyList<int> RawLengths,
    [property: JsonPropertyName("result_fields")] IReadOnlyList<string> ResultFields,
    [property: JsonPropertyName("response_fields")] IReadOnlyDictionary<string, JsonElement> ResponseFields,
    [property: JsonPropertyName("headers")] IReadOnlyDictionary<string, string> Headers,
    [property: JsonPropertyName("at")] DateTimeOffset At);

/// <summary>Outcome of the Tavily probe (contract stage 4, 3.2).</summary>
public enum ProbeDecision { Continue, ContinueWithBearer, HaltKey, HaltRawContent, NotEvaluable }

/// <summary>
/// The Tavily probe VT: one <c>POST search</c> per topic exactly as contract stage 4, 4.3 asks (<c>search_depth</c> basic,
/// <c>max_results</c> 3, <c>include_raw_content</c> true, <c>include_answer</c> false), key in the body. After a 401 or 403 with the
/// key in the body the next request sends it as <c>Authorization: Bearer</c> instead. At most <see cref="Ceiling"/> requests per file,
/// each one counts. The key never appears in a line (every line is redacted).
/// </summary>
public sealed class TavilyProbe(HttpClient http, string apiKey, ICodexGuard guard, TimeProvider time, TextWriter log)
{
    /// <summary>Most requests one probe file may hold (contract stage 4, 3.2: cap 3 searches).</summary>
    public const int Ceiling = 3;

    /// <summary>Results asked for per search.</summary>
    public const int MaxResults = 3;

    /// <summary>Key in the JSON body, as the grounding provider sends it today.</summary>
    public const string BodyAuth = "body";

    /// <summary>Key as bearer token.</summary>
    public const string BearerAuth = "bearer";

    private static readonly HashSet<string> ContentFields = new(StringComparer.OrdinalIgnoreCase) { "results", "answer", "images", "follow_up_questions" };

    private static readonly HashSet<string> SecretHeaders = new(StringComparer.OrdinalIgnoreCase) { "set-cookie", "authorization", "cookie", "proxy-authorization" };

    /// <summary>The topics of the probe: the first topic the query gate sends for each development order of VR, in id order, at most three.</summary>
    public static IReadOnlyList<(string Order, string Topic)> Topics(IReadOnlyList<ResearchOrder> orders, ResearchKit kit, int maxSearches)
    {
        var topics = new List<(string, string)>();
        foreach (var order in ResearchCorpus.Set(orders, ResearchCorpus.DevSet))
        {
            var sent = kit.Select(order.Topics, order.Request, order.FileTexts, order.ProfileText, maxSearches).Sent;
            if (sent.Count > 0)
                topics.Add((order.Id, sent[0]));
            if (topics.Count == Ceiling)
                break;
        }
        return topics;
    }

    /// <summary>The JSON body of one probe request; the key only in <see cref="BodyAuth"/> mode.</summary>
    public static string Body(string topic, string? bodyKey)
    {
        var body = new Dictionary<string, object>(StringComparer.Ordinal);
        if (bodyKey is not null)
            body["api_key"] = bodyKey;
        body["query"] = topic;
        body["search_depth"] = "basic";
        body["max_results"] = MaxResults;
        body["include_raw_content"] = true;
        body["include_answer"] = false;
        return JsonSerializer.Serialize(body);
    }

    /// <summary>Sends the probe requests still missing in <paramref name="outPath"/> (at most <paramref name="maxSearches"/> in this run) and returns the exit code of the decision.</summary>
    public async Task<int> RunAsync(IReadOnlyList<(string Order, string Topic)> topics, string outPath, int maxSearches, CancellationToken ct)
    {
        var previous = JsonLines.Load<TavilyProbeLine>(outPath);
        var budget = new CallBudget(maxSearches, Ceiling, previous.Count);
        var lines = previous.ToList();
        foreach (var (order, topic) in topics)
        {
            while (Pending(lines, topic) is { } auth)
            {
                if (guard.Blocker() is { } blocker)
                {
                    log.WriteLine($"Stopped before the probe of {order}: {blocker}. Continue later with the same command.");
                    return ResearchRunner.Blocked;
                }
                if (!budget.TryTake())
                    return Report(lines);
                var line = await SendAsync(order, topic, auth, ct);
                JsonLines.Append(outPath, line, [apiKey]);
                lines.Add(line);
                log.WriteLine($"{order}: {auth} → {(line.HttpStatus?.ToString() ?? line.Error)} in {line.DurationMs} ms, {line.ResultsWithRaw}/{line.Results} results with raw text");
                if (line.Auth == BearerAuth && IsAuthFailure(line.HttpStatus))
                    return Report(lines);
            }
        }
        return Report(lines);
    }

    /// <summary>Decision of contract stage 4, 3.2 over all lines of a probe file, with the figures behind it.</summary>
    public static (ProbeDecision Decision, string Detail) Decide(IReadOnlyList<TavilyProbeLine> lines)
    {
        if (lines.Count == 0)
            return (ProbeDecision.NotEvaluable, "no probe request in the file");
        var bodyRejected = lines.Any(l => l.Auth == BodyAuth && IsAuthFailure(l.HttpStatus));
        var bearer = lines.Where(l => l.Auth == BearerAuth).ToList();
        var ok = lines.Where(l => l.HttpStatus is >= 200 and < 300).ToList();
        var results = ok.Sum(l => l.Results);
        var withRaw = ok.Sum(l => l.ResultsWithRaw);
        var lengths = ok.SelectMany(l => l.RawLengths).Where(n => n > 0).Order().ToList();
        var figures = $"{ok.Count}/{lines.Count} requests 2xx, {withRaw}/{results} results with raw_content"
            + (lengths.Count > 0 ? $" (length median {EvalBasics.Percentile(lengths.Select(n => (double)n).ToList(), 0.5):0}, max {lengths[^1]})" : string.Empty)
            + $", duration median {EvalBasics.Percentile(lines.Select(l => (double)l.DurationMs).Order().ToList(), 0.5):0} ms; {Usage(lines)}";

        if (bodyRejected && bearer.Count > 0 && bearer.All(l => IsAuthFailure(l.HttpStatus)))
            return (ProbeDecision.HaltKey, $"the key was refused in the body and as bearer token: halt, research stays Off, the operator checks the key. {figures}");
        if (bodyRejected && bearer.Count == 0)
            return (ProbeDecision.NotEvaluable, $"the key was refused in the body; the bearer request is still missing. {figures}");
        if (ok.Count == 0 || results == 0)
            return (ProbeDecision.NotEvaluable, $"no successful search with results. {figures}");
        if (withRaw * 3 < results * 2)
            return (ProbeDecision.HaltRawContent, $"raw_content is mostly empty: halt, Tavily snippets are no fallback; a new decision is needed (for example /extract). {figures}");
        return bodyRejected
            ? (ProbeDecision.ContinueWithBearer, $"the bearer header works where the body key was refused: F2 sends the key as header; continue with VR. {figures}")
            : (ProbeDecision.Continue, $"continue with VR. {figures}");
    }

    /// <summary>Exit code of a decision: 0 continue, 1 halt, 4 not evaluable.</summary>
    public static int ExitCode(ProbeDecision decision) => decision switch
    {
        ProbeDecision.Continue or ProbeDecision.ContinueWithBearer => 0,
        ProbeDecision.HaltKey or ProbeDecision.HaltRawContent => 1,
        _ => 4,
    };

    private static string Usage(IReadOnlyList<TavilyProbeLine> lines)
    {
        var fields = lines.SelectMany(l => l.ResponseFields.Where(f => IsUsage(f.Key)).Select(f => $"{f.Key}={f.Value.GetRawText()}"))
            .Concat(lines.SelectMany(l => l.Headers.Where(h => IsUsage(h.Key)).Select(h => $"header {h.Key}={h.Value}")))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return fields.Count == 0 ? "credit use unknown (the answers name none)" : $"usage reported: {string.Join("; ", fields)}";
    }

    private static bool IsUsage(string name) =>
        name.Contains("credit", StringComparison.OrdinalIgnoreCase) || name.Contains("usage", StringComparison.OrdinalIgnoreCase)
        || name.Contains("ratelimit", StringComparison.OrdinalIgnoreCase) || name.Contains("rate-limit", StringComparison.OrdinalIgnoreCase)
        || name.Contains("quota", StringComparison.OrdinalIgnoreCase) || name.Contains("cost", StringComparison.OrdinalIgnoreCase);

    private int Report(IReadOnlyList<TavilyProbeLine> lines)
    {
        var (decision, detail) = Decide(lines);
        log.WriteLine($"VT {decision}: {detail}");
        return ExitCode(decision);
    }

    private static string? Pending(IReadOnlyList<TavilyProbeLine> lines, string topic)
    {
        if (lines.Any(l => l.Auth == BearerAuth && IsAuthFailure(l.HttpStatus)))
            return null;
        var mode = lines.Any(l => l.Auth == BodyAuth && IsAuthFailure(l.HttpStatus)) ? BearerAuth : BodyAuth;
        return lines.Any(l => l.Topic == topic && l.Auth == mode) || lines.Any(l => l.Topic == topic && !IsAuthFailure(l.HttpStatus)) ? null : mode;
    }

    private static bool IsAuthFailure(int? status) => status is 401 or 403;

    private async Task<TavilyProbeLine> SendAsync(string order, string topic, string auth, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "search")
        {
            Content = new StringContent(Body(topic, auth == BodyAuth ? apiKey : null), Encoding.UTF8, "application/json"),
        };
        if (auth == BearerAuth)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var started = time.GetTimestamp();
        try
        {
            using var response = await http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            var duration = (int)time.GetElapsedTime(started).TotalMilliseconds;
            var headers = response.Headers.Concat(response.Content.Headers)
                .Where(h => !SecretHeaders.Contains(h.Key))
                .ToDictionary(h => h.Key.ToLowerInvariant(), h => string.Join(", ", h.Value), StringComparer.Ordinal);
            return Read(order, topic, auth, (int)response.StatusCode, duration, text, headers);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new TavilyProbeLine(order, topic, auth, null, ex.GetType().Name, (int)time.GetElapsedTime(started).TotalMilliseconds, 0, 0, [], [],
                new Dictionary<string, JsonElement>(), new Dictionary<string, string>(), time.GetUtcNow());
        }
    }

    private TavilyProbeLine Read(string order, string topic, string auth, int status, int duration, string text, IReadOnlyDictionary<string, string> headers)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var resultFields = new SortedSet<string>(StringComparer.Ordinal);
        var lengths = new List<int>();
        var results = 0;
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (!ContentFields.Contains(property.Name))
                        fields[property.Name] = property.Value.Clone();
                }
                if (document.RootElement.TryGetProperty("results", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var result in list.EnumerateArray())
                    {
                        results++;
                        if (result.ValueKind != JsonValueKind.Object)
                            continue;
                        foreach (var property in result.EnumerateObject())
                            resultFields.Add(property.Name);
                        lengths.Add(result.TryGetProperty("raw_content", out var raw) && raw.ValueKind == JsonValueKind.String ? raw.GetString()!.Trim().Length : 0);
                    }
                }
            }
        }
        catch (JsonException)
        {
            fields["unreadable_body_length"] = JsonSerializer.SerializeToElement(text.Length);
        }
        return new TavilyProbeLine(order, topic, auth, status, null, duration, results, lengths.Count(n => n > 0), lengths, resultFields.ToList(), fields, headers, time.GetUtcNow());
    }
}
