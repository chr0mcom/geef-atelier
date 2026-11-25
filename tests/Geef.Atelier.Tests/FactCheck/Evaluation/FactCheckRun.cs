using System.Text.Json;
using System.Text.Json.Serialization;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Writing;
using Geef.Atelier.Infrastructure.Pipeline.Direct.FactCheck;

namespace Geef.Atelier.Tools.FactCheckEval;

/// <summary>
/// The parts of the real fact check the measurement uses: prompts and schema (for the input fingerprint), parser, filter,
/// quote search and the arithmetic check of the files. <see cref="Real"/> is the production code; tests swap single parts.
/// </summary>
public sealed record FactCheckKit(
    string SystemPrompt,
    string SchemaName,
    string SchemaJson,
    Func<FactMaterial, string, string> UserPrompt,
    Func<string?, IReadOnlyList<ProposedFactFinding>?> Parse,
    Func<IReadOnlyList<ProposedFactFinding>, string, FactMaterial, FactFilterResult> Filter,
    Func<string, string, (int Start, int Length)?> Locate,
    Func<IReadOnlyList<(string SourceId, string FileName, string Text)>, SourceArithmeticResult> Arithmetic)
{
    /// <summary>The production code: <c>FactCheckPrompts</c>, <c>FactCheckSchema</c>, <c>FactCheckOutputParser</c>, <c>FactFindingFilter</c>, <c>QuoteVerifier</c>, <c>SourceArithmeticCheck</c>.</summary>
    public static FactCheckKit Real { get; } = new(
        FactCheckPrompts.System,
        FactCheckSchema.CheckName,
        FactCheckSchema.CheckJson,
        FactCheckPrompts.User,
        FactCheckOutputParser.ParseCheck,
        FactFindingFilter.Apply,
        (haystack, quote) => QuoteVerifier.Find(haystack, quote, allowApproximate: true) is { IsFound: true } m ? (m.Start, m.Length) : null,
        SourceArithmeticCheck.Check);

    /// <summary>
    /// Fingerprint of everything the checker is given for <paramref name="item"/>: system prompt, schema name, schema and user
    /// prompt (material and text). A change of any of them makes older results not match.
    /// </summary>
    public string InputHash(FactCheckItem item) => EvalBasics.Fingerprint(SystemPrompt, SchemaName, SchemaJson, UserPrompt(item.Material, item.Text));
}

/// <summary>
/// One line of a fact-check result file: one call (every attempt, also a failed one) with status, model, duration, raw answer,
/// the time limit it ran with and the input fingerprint.
/// </summary>
public sealed record FactCheckLine(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("split")] string Split,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("duration_ms")] int DurationMs,
    [property: JsonPropertyName("error_code")] string? ErrorCode,
    [property: JsonPropertyName("raw")] string? Raw,
    [property: JsonPropertyName("timeout_ms")] int TimeoutMs,
    [property: JsonPropertyName("reasoning_effort")] string? ReasoningEffort,
    [property: JsonPropertyName("prompt_version")] string? PromptVersion,
    [property: JsonPropertyName("input_hash")] string? InputHash,
    [property: JsonPropertyName("at")] DateTimeOffset At);

/// <summary>Status of a call line.</summary>
public static class CallStatus
{
    /// <summary>The model answered with a readable list.</summary>
    public const string Ok = "Ok";

    /// <summary>The model answered, but without usable JSON (also a refusal).</summary>
    public const string Invalid = "Invalid";

    /// <summary>No answer within the time limit.</summary>
    public const string TimedOut = "TimedOut";

    /// <summary>Any other failure (gateway, limit, network).</summary>
    public const string Failed = "Failed";

    /// <summary>Error codes of <c>LlmFactChecker</c> for an answer without usable JSON (same values as the checker).</summary>
    public static readonly IReadOnlySet<string> InvalidCodes = new HashSet<string>(StringComparer.Ordinal) { "no_json", "unusable_json" };

    /// <summary>Error code of <c>LlmFactChecker</c> for a call that ran into its time limit.</summary>
    public const string TimeoutCode = "timeout";

    /// <summary>The status of a checker call.</summary>
    public static string Of(string? errorCode, bool hasFindings) => errorCode switch
    {
        null when hasFindings => Ok,
        null => Invalid,
        TimeoutCode => TimedOut,
        _ when InvalidCodes.Contains(errorCode) => Invalid,
        _ => Failed,
    };

    /// <summary>True for lines with a latency: answers (also unusable ones) and time-outs; fast failures never count.</summary>
    public static bool HasLatency(string status) => status is Ok or Invalid or TimedOut;
}

/// <summary>Reading and appending result files.</summary>
public static class ResultFile
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>All lines of a result file in their order; a missing file has none.</summary>
    public static IReadOnlyList<T> Load<T>(string path) =>
        File.Exists(path)
            ? File.ReadLines(path).Where(l => l.TrimStart().StartsWith('{')).Select(l => JsonSerializer.Deserialize<T>(l, Json)!).ToList()
            : [];

    /// <summary>Appends one line and flushes it to disk, so an abort never loses a counted call.</summary>
    public static void Append<T>(string path, T line)
    {
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream);
        writer.Write(JsonSerializer.Serialize(line, Json) + "\n");
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }
}

/// <summary>Settings of one <c>run</c>.</summary>
public sealed record FactCheckRunSettings(string CorpusPath, string OutPath, string Split, int MaxCalls, TimeSpan Timeout, string? ReasoningEffort);

/// <summary>
/// Calls the real fact checker for every text of a corpus split, one after another (never in parallel, the subscription is shared),
/// and appends one line per attempt. Before every call the <see cref="ICodexGuard"/> is asked; when it blocks, the run stops with
/// exit code 3 and can be continued later with the same command. Texts whose last result for the current input is <c>Ok</c> or
/// <c>Invalid</c> are skipped; failed attempts stay in the file and count in the evaluation, so repeating never improves it.
/// </summary>
internal sealed class FactCheckRunner(FactCheckRunSettings settings, IFactChecker checker, ReviewerProfile profile, ICodexGuard guard, FactCheckKit kit, TextWriter log)
{
    /// <summary>Most attempts one result file may hold (contract stage 3, 3.2: at most 52 Codex calls for V2).</summary>
    public const int Ceiling = 52;

    /// <summary>Exit code when the guard stopped the run before a call.</summary>
    public const int Blocked = 3;

    /// <summary>Exit code when texts are left because the cap is reached or calls failed.</summary>
    public const int Incomplete = 4;

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var bases = FactCheckCorpus.Load(settings.CorpusPath);
        var corpusProblems = FactCheckCorpus.Problems(bases);
        if (corpusProblems.Count > 0)
        {
            log.WriteLine($"The corpus cannot be measured: {string.Join("; ", corpusProblems)}");
            return 2;
        }

        var previous = ResultFile.Load<FactCheckLine>(settings.OutPath);
        var items = FactCheckCorpus.Items(bases).Where(i => settings.Split == "all" || i.Split == settings.Split).ToList();
        var hashes = items.ToDictionary(i => i.Id, kit.InputHash, StringComparer.Ordinal);
        var done = FactCheckEvaluation.Answered(previous, hashes);
        var stale = previous.Where(l => hashes.TryGetValue(l.Id, out var h) && l.InputHash != h).Select(l => l.Id).Distinct(StringComparer.Ordinal).Count();
        if (stale > 0)
            log.WriteLine($"Warning: {stale} texts in {settings.OutPath} have results for another input; they are called again, but the file stays not evaluable. Use a new --out file.");

        var budget = new CallBudget(settings.MaxCalls, Ceiling, previous.Count);
        var todo = items.Where(i => !done.Contains(i.Id)).ToList();
        log.WriteLine($"{todo.Count} texts to call ({done.Count} already done; cap {settings.MaxCalls} for this run, {previous.Count} of {Ceiling} calls already in the file).");

        var failures = 0;
        foreach (var item in todo)
        {
            if (guard.Blocker() is { } blocker)
            {
                log.WriteLine($"Stopped before {item.Id}: {blocker}. Continue later with the same command.");
                return Blocked;
            }
            if (!budget.TryTake())
            {
                log.WriteLine($"Call cap reached after {budget.Used} calls; {todo.Count - todo.IndexOf(item)} texts left.");
                return Incomplete;
            }

            var call = await checker.CheckAsync(item.Material, item.Text, profile, settings.Timeout, 1, ct);
            var status = CallStatus.Of(call.ErrorCode, call.Proposed is not null);
            ResultFile.Append(settings.OutPath, new FactCheckLine(
                item.Id, item.Split, status, call.Model, call.DurationMs, call.ErrorCode, call.RawJson,
                (int)settings.Timeout.TotalMilliseconds, settings.ReasoningEffort, FactCheckPrompts.Version, hashes[item.Id], DateTimeOffset.UtcNow));
            if (status is not (CallStatus.Ok or CallStatus.Invalid))
                failures++;
            log.WriteLine($"{item.Id}: {status} {call.DurationMs} ms{(call.ErrorCode is null ? string.Empty : $" ({call.ErrorCode})")}, {call.Proposed?.Count ?? 0} proposed findings");
        }

        log.WriteLine($"Finished: {budget.Used} calls, {failures} failed.");
        return failures == 0 ? 0 : Incomplete;
    }
}
