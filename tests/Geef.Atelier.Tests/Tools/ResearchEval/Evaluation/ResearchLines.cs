using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Geef.Atelier.Tools.ResearchEval;

/// <summary>One search of an order: topic number (from 1), error code, duration and the number of results with raw text.</summary>
public sealed record SearchRecord(
    [property: JsonPropertyName("topic")] int Topic,
    [property: JsonPropertyName("error_code")] string? ErrorCode,
    [property: JsonPropertyName("duration_ms")] int DurationMs,
    [property: JsonPropertyName("hits")] int Hits);

/// <summary>A page with its raw text as the search client returned it (already cut to the configured length).</summary>
public sealed record PageRecord(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("topic")] int Topic,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("raw_text")] string RawText,
    [property: JsonPropertyName("raw_sha256")] string RawSha256);

/// <summary>A passage as the model proposed it.</summary>
public sealed record PassageRecord(
    [property: JsonPropertyName("topic")] int Topic,
    [property: JsonPropertyName("page")] string Page,
    [property: JsonPropertyName("quote")] string Quote);

/// <summary>The one call that picks passages: model, error code, duration, raw answer and what the parser kept or dropped.</summary>
public sealed record CondenseRecord(
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("error_code")] string? ErrorCode,
    [property: JsonPropertyName("duration_ms")] int DurationMs,
    [property: JsonPropertyName("raw")] string? Raw,
    [property: JsonPropertyName("proposed")] IReadOnlyList<PassageRecord> Proposed,
    [property: JsonPropertyName("parser_dropped")] IReadOnlyDictionary<string, int> ParserDropped);

/// <summary>An entry of the dossier as <c>DossierBuilder</c> stored it.</summary>
public sealed record EntryRecord(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("topic")] int Topic,
    [property: JsonPropertyName("quote")] string Quote,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("retrieved_at")] DateTimeOffset RetrievedAt,
    [property: JsonPropertyName("match")] string Match,
    [property: JsonPropertyName("raw_sha256")] string RawSha256,
    [property: JsonPropertyName("start")] int Start,
    [property: JsonPropertyName("length")] int Length);

/// <summary>The research settings a line was measured with; they are part of the input fingerprint.</summary>
public sealed record RunSettingsRecord(
    [property: JsonPropertyName("max_searches")] int MaxSearches,
    [property: JsonPropertyName("results_per_search")] int ResultsPerSearch,
    [property: JsonPropertyName("raw_chars_per_page")] int RawCharsPerPage,
    [property: JsonPropertyName("max_entries")] int MaxEntries,
    [property: JsonPropertyName("search_timeout_ms")] int SearchTimeoutMs,
    [property: JsonPropertyName("condense_timeout_ms")] int CondenseTimeoutMs);

/// <summary>
/// One line of a VR result file: one attempt for one order (every attempt counts, also a failed one), with the raw texts of the pages,
/// the proposals, the stored entries, the calls the transport counted and the input fingerprint.
/// </summary>
public sealed record ResearchRunLine(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("set")] string Set,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("error_code")] string? ErrorCode,
    [property: JsonPropertyName("topics")] IReadOnlyList<string> Topics,
    [property: JsonPropertyName("sent")] IReadOnlyList<string> Sent,
    [property: JsonPropertyName("withheld")] int Withheld,
    [property: JsonPropertyName("over_limit")] int OverLimit,
    [property: JsonPropertyName("tavily_requests")] int TavilyRequests,
    [property: JsonPropertyName("claude_calls")] int ClaudeCalls,
    [property: JsonPropertyName("searches")] IReadOnlyList<SearchRecord> Searches,
    [property: JsonPropertyName("pages")] IReadOnlyList<PageRecord> Pages,
    [property: JsonPropertyName("condense")] CondenseRecord? Condense,
    [property: JsonPropertyName("entries")] IReadOnlyList<EntryRecord> Entries,
    [property: JsonPropertyName("dropped")] IReadOnlyDictionary<string, int> Dropped,
    [property: JsonPropertyName("search_ms")] int SearchMs,
    [property: JsonPropertyName("duration_ms")] int DurationMs,
    [property: JsonPropertyName("retrieved_at")] DateTimeOffset RetrievedAt,
    [property: JsonPropertyName("settings")] RunSettingsRecord Settings,
    [property: JsonPropertyName("input_hash")] string? InputHash,
    [property: JsonPropertyName("at")] DateTimeOffset At);

/// <summary>Reading and appending JSON-lines files; every written line has the secrets of the run removed.</summary>
public static class JsonLines
{
    private static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Text that replaces a secret in a written line.</summary>
    public const string Redacted = "[redacted]";

    /// <summary>All lines of a file in their order; a missing file has none.</summary>
    public static IReadOnlyList<T> Load<T>(string path) =>
        File.Exists(path)
            ? File.ReadLines(path).Where(l => l.TrimStart().StartsWith('{')).Select(l => JsonSerializer.Deserialize<T>(l, Options)!).ToList()
            : [];

    /// <summary>One line as JSON with every secret of <paramref name="secrets"/> (from 8 characters on) replaced by <see cref="Redacted"/>.</summary>
    public static string Serialize<T>(T line, IEnumerable<string?> secrets)
    {
        var json = JsonSerializer.Serialize(line, Options);
        foreach (var secret in secrets)
        {
            if (secret is { Length: >= 8 })
                json = json.Replace(secret, Redacted, StringComparison.Ordinal).Replace(JsonEncodedText.Encode(secret, Options.Encoder).Value, Redacted, StringComparison.Ordinal);
        }
        return json;
    }

    /// <summary>Appends one line and flushes it to disk, so an abort never loses a counted call.</summary>
    public static void Append<T>(string path, T line, IEnumerable<string?> secrets)
    {
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream);
        writer.Write(Serialize(line, secrets) + "\n");
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    /// <summary>Lower-case hex SHA-256 of the UTF-8 bytes of <paramref name="text"/>.</summary>
    public static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>Lower-case hex SHA-256 of a file's bytes.</summary>
    public static string FileSha256(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}

/// <summary>
/// Raw page texts are content of third parties and stay out of Git (contract stage 4, 3): result files must lie outside every Git work
/// tree, for example under <c>EVAL_RESEARCH_DIR</c>.
/// </summary>
public static class OutputLocation
{
    /// <summary>Why <paramref name="path"/> may not hold results (it lies in a Git work tree), or null.</summary>
    public static string? Problem(string path)
    {
        for (var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path)) ?? "/"); dir is not null; dir = dir.Parent)
        {
            var git = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return $"{path} lies in the Git work tree {dir.FullName}; raw page texts must stay outside Git (use a directory such as $EVAL_RESEARCH_DIR)";
        }
        return null;
    }
}
