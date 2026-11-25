using System.Diagnostics;
using System.Text.Json.Serialization;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Writing;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Pipeline.Direct;

namespace Geef.Atelier.Tools.FactCheckEval;

/// <summary>A file of a probe order.</summary>
public sealed record ProbeFile(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("text")] string Text);

/// <summary>A synthetic order of the writer-notes probe (V1): <c>short</c> (about 500 words) or <c>long</c> (about 6,000 words).</summary>
public sealed record ProbeOrder(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("size")] string Size,
    [property: JsonPropertyName("lang")] string Lang,
    [property: JsonPropertyName("request")] string Request,
    [property: JsonPropertyName("target_words")] int TargetWords,
    [property: JsonPropertyName("files")] IReadOnlyList<ProbeFile> Files);

/// <summary>
/// One line of a probe result file: one writer call (every attempt) with the split status, the damage found, the raw answer,
/// length, last line before the block, duration, output tokens, time limit and input fingerprint.
/// </summary>
public sealed record ProbeLine(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("size")] string Size,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("damage")] IReadOnlyList<string> Damage,
    [property: JsonPropertyName("words")] int Words,
    [property: JsonPropertyName("target_words")] int TargetWords,
    [property: JsonPropertyName("last_line")] string? LastLine,
    [property: JsonPropertyName("duration_ms")] int DurationMs,
    [property: JsonPropertyName("output_tokens")] int? OutputTokens,
    [property: JsonPropertyName("finish_reason")] string? FinishReason,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("error_code")] string? ErrorCode,
    [property: JsonPropertyName("raw")] string? Raw,
    [property: JsonPropertyName("timeout_ms")] int TimeoutMs,
    [property: JsonPropertyName("input_hash")] string InputHash,
    [property: JsonPropertyName("at")] DateTimeOffset At);

/// <summary>What the split of one answer shows: status, text, damage and the figures of the line.</summary>
public sealed record ProbeReading(string Status, IReadOnlyList<string> Damage, int Words, string? LastLine);

/// <summary>Builds the real writer call of a probe order and reads its answer.</summary>
public static class NotesProbe
{
    /// <summary>Damage: a rest of the block (marker, JSON start or comment end) stays in the text of the person.</summary>
    public const string Residue = "residue";

    /// <summary>Damage: text follows the block, or there is more than one block, so the block sat in the middle of the text.</summary>
    public const string BlockInsideText = "block_inside_text";

    /// <summary>Damage: the answer was cut off at the token limit.</summary>
    public const string Truncated = "truncated";

    /// <summary>Statuses of attempts without an answer.</summary>
    public const string TimedOut = "TimedOut", Failed = "Failed";

    private static readonly string[] ResidueMarkers = ["atelier-notes", "atelier‑notes", "{\"assumptions\"", "-->"];

    /// <summary>The writer profile of the direct path: Opus, document mode, the real system prompt.</summary>
    public static ExecutorProfile Writer => SystemCrew.DirectWriterProfile;

    /// <summary>The background document of the order, rendered by the real <c>SourceBlockRenderer</c> with ids <c>S1…</c>.</summary>
    internal static OrderSourceBundle Bundle(ProbeOrder order)
    {
        var sources = order.Files.Select((f, i) => new PromptSource(
            Guid.ParseExact(EvalBasics.Fingerprint(order.Id, f.Name)[..32], "N"), $"S{i + 1}", f.Name, Kind(f.Kind), null, null, f.Text, f.Text.Length)).ToList();
        return SourceBlockRenderer.Render(sources, new SourceOptions());
    }

    /// <summary>
    /// The first writing pass exactly as <c>DirectWriterExecutor</c> sends it for a CLI writer: system prompt, user prompt with the
    /// notes instruction (<c>writerNotes: true</c>), empty draft, files as background document, proxy failover off.
    /// </summary>
    internal static LlmRequest Request(ProbeOrder order, ExecutorProfile writer)
    {
        var bundle = Bundle(order);
        var documentMode = ModelNameNormalizer.IsCliProvider(writer.Provider);
        return new LlmRequest
        {
            Model = ModelNameNormalizer.Normalize(writer.Provider, writer.Model),
            SystemPrompt = DirectWriterPrompts.SystemPrompt(writer.SystemPrompt, documentMode),
            UserPrompt = DirectWriterPrompts.FirstPass(order.Request, bundle, documentMode, seed: null, writerNotes: true),
            MaxTokens = writer.MaxTokens ?? new LlmOptions().DefaultMaxTokens,
            DocumentMode = documentMode,
            Document = documentMode ? string.Empty : null,
            ContextDocument = documentMode && bundle.ContextDocument.Length > 0 ? bundle.ContextDocument : null,
            DisableFailover = documentMode,
        };
    }

    /// <summary>Fingerprint of the call: model, system prompt, user prompt and background document.</summary>
    internal static string InputHash(LlmRequest request) =>
        EvalBasics.Fingerprint(request.Model, request.SystemPrompt, request.UserPrompt, request.ContextDocument);

    /// <summary>Splits <paramref name="raw"/> with <paramref name="split"/> and looks for damage.</summary>
    public static ProbeReading Read(string raw, string? finishReason, Func<string, WriterNotesSplit> split)
    {
        var result = split(raw);
        var damage = new List<string>();
        if (ResidueMarkers.Any(m => result.Text.Contains(m, StringComparison.Ordinal)))
            damage.Add(Residue);
        if (BlockSitsInsideText(raw))
            damage.Add(BlockInsideText);
        if (string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase))
            damage.Add(Truncated);
        var lastLine = result.Text.TrimEnd().Split('\n').LastOrDefault(l => l.Trim().Length > 0)?.Trim();
        if (lastLine is { Length: > 200 })
            lastLine = lastLine[..200];
        return new ProbeReading(result.Status.ToString(), damage, Words(result.Text), lastLine);
    }

    /// <summary>True when the answer has more than one block or text after the end of its last block.</summary>
    public static bool BlockSitsInsideText(string raw)
    {
        var lines = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var starts = lines.Select((l, i) => (l, i)).Where(x => x.l.StartsWith(WriterNotesFormat.StartMarker, StringComparison.Ordinal)).Select(x => x.i).ToList();
        if (starts.Count == 0)
            return false;
        if (starts.Count > 1)
            return true;
        var after = string.Join('\n', lines.Skip(starts[0]));
        var end = after.IndexOf(WriterNotesFormat.EndMarker, StringComparison.Ordinal);
        return end >= 0 && after[(end + WriterNotesFormat.EndMarker.Length)..].Trim().Length > 0;
    }

    /// <summary>Words separated by white space.</summary>
    public static int Words(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static SourceKind Kind(string kind) => kind.ToLowerInvariant() switch
    {
        "pdf" => SourceKind.Pdf,
        "docx" => SourceKind.Docx,
        "markdown" => SourceKind.Markdown,
        _ => SourceKind.Text,
    };
}

/// <summary>Settings of one <c>probe-notes</c> run: the cumulative number of short and long attempts the file shall hold.</summary>
public sealed record ProbeSettings(string InputsPath, string OutPath, int Short, int Long, int MaxCalls, TimeSpan Timeout);

/// <summary>
/// Runs the real writer call of the probe orders until the result file holds <see cref="ProbeSettings.Short"/> short and
/// <see cref="ProbeSettings.Long"/> long attempts (cumulative, so the stages a, b and c of contract stage 3, 3.1 are the same
/// command with larger numbers). Short attempts go round the short orders, each order as rarely as possible. One attempt per call,
/// no retry; a failed or timed-out attempt counts like any other. Before every call the <see cref="ICodexGuard"/> is asked.
/// <paramref name="request"/> builds the call of an order (in the tool <see cref="NotesProbe.Request"/>).
/// </summary>
internal sealed class NotesProbeRunner(ProbeSettings settings, ILlmClient client, Func<ProbeOrder, LlmRequest> request, ICodexGuard guard, Func<string, WriterNotesSplit> split, TextWriter log)
{
    /// <summary>Most attempts one result file may hold (contract: at most 20 Opus calls for V1).</summary>
    public const int Ceiling = 20;

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var orders = ResultFile.Load<ProbeOrder>(settings.InputsPath);
        var previous = ResultFile.Load<ProbeLine>(settings.OutPath).ToList();
        var requests = orders.ToDictionary(o => o.Id, request, StringComparer.Ordinal);
        var stale = previous.Where(l => !requests.TryGetValue(l.Id, out var r) || NotesProbe.InputHash(r) != l.InputHash).Select(l => l.Id).Distinct(StringComparer.Ordinal).ToList();
        if (stale.Count > 0)
        {
            log.WriteLine($"{settings.OutPath} has results for another input or unknown orders ({EvalBasics.Few(stale)}); use a new --out file.");
            return 2;
        }

        var plan = Plan(orders, previous);
        var budget = new CallBudget(settings.MaxCalls, Ceiling, previous.Count);
        log.WriteLine($"{plan.Count} attempts to make ({previous.Count} in the file, cap {settings.MaxCalls} for this run, {Ceiling} per file).");
        foreach (var order in plan)
        {
            if (guard.Blocker() is { } blocker)
            {
                log.WriteLine($"Stopped before {order.Id}: {blocker}. Continue later with the same command.");
                return FactCheckRunner.Blocked;
            }
            if (!budget.TryTake())
            {
                log.WriteLine($"Call cap reached after {budget.Used} calls.");
                return FactCheckRunner.Incomplete;
            }
            var line = await CallAsync(order, requests[order.Id], ct);
            ResultFile.Append(settings.OutPath, line);
            previous.Add(line);
            log.WriteLine($"{order.Id}: {line.Status}{(line.Damage.Count > 0 ? " DAMAGE " + string.Join(",", line.Damage) : string.Empty)}, {line.Words} words, {line.DurationMs} ms");
        }
        return 0;
    }

    /// <summary>The orders still to call so that the file holds the wanted numbers of short and long attempts.</summary>
    internal IReadOnlyList<ProbeOrder> Plan(IReadOnlyList<ProbeOrder> orders, IReadOnlyList<ProbeLine> previous)
    {
        var plan = new List<ProbeOrder>();
        foreach (var (size, wanted) in new[] { ("short", settings.Short), ("long", settings.Long) })
        {
            var pool = orders.Where(o => o.Size == size).ToList();
            if (pool.Count == 0)
                continue;
            var counts = pool.ToDictionary(o => o.Id, o => previous.Count(l => l.Id == o.Id), StringComparer.Ordinal);
            for (var have = counts.Values.Sum(); have < wanted; have++)
            {
                var next = pool.OrderBy(o => counts[o.Id]).ThenBy(o => pool.IndexOf(o)).First();
                counts[next.Id]++;
                plan.Add(next);
            }
        }
        return plan;
    }

    private async Task<ProbeLine> CallAsync(ProbeOrder order, LlmRequest request, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        using var timeout = new CancellationTokenSource(settings.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        var hash = NotesProbe.InputHash(request);
        var limit = (int)settings.Timeout.TotalMilliseconds;
        try
        {
            var response = await client.CompleteAsync(request, linked.Token);
            var reading = NotesProbe.Read(response.Text, response.FinishReason, split);
            return new ProbeLine(order.Id, order.Size, reading.Status, reading.Damage, reading.Words, order.TargetWords, reading.LastLine,
                (int)watch.ElapsedMilliseconds, response.TokenUsage.OutputTokens, response.FinishReason, request.Model, null, response.Text, limit, hash, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failure(order, request, NotesProbe.TimedOut, "timeout", watch, limit, hash);
        }
        catch (HttpRequestException ex)
        {
            return Failure(order, request, NotesProbe.Failed, ex.ErrorCode() ?? (ex.StatusCode is { } s ? $"http_{(int)s}" : "http"), watch, limit, hash);
        }
    }

    private static ProbeLine Failure(ProbeOrder order, LlmRequest request, string status, string code, Stopwatch watch, int limit, string hash) =>
        new(order.Id, order.Size, status, [], 0, order.TargetWords, null, (int)watch.ElapsedMilliseconds, null, null, request.Model, code, null, limit, hash, DateTimeOffset.UtcNow);
}
