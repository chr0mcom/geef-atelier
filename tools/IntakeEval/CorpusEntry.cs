using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Geef.Atelier.Tools.IntakeEval;

/// <summary>A file of a corpus entry; the content is always plain text, <see cref="Kind"/> is only a label.</summary>
public sealed record CorpusFile(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("pages")] int? Pages);

/// <summary>The profile of the person in a corpus entry.</summary>
public sealed record CorpusProfile(
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("organization")] string? Organization,
    [property: JsonPropertyName("formality")] string? Formality,
    [property: JsonPropertyName("language")] string? Language,
    [property: JsonPropertyName("signature")] string? Signature,
    [property: JsonPropertyName("no_gos")] string? NoGos,
    [property: JsonPropertyName("free_text")] string? FreeText);

/// <summary>One synthetic order of the intake corpus (written by <c>tools/Eval/geef_eval/intake_corpus.py</c>).</summary>
public sealed record CorpusEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("corpus")] string Corpus,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("lang")] string Lang,
    [property: JsonPropertyName("request")] string Request,
    [property: JsonPropertyName("files")] IReadOnlyList<CorpusFile>? Files,
    [property: JsonPropertyName("profile")] CorpusProfile? Profile,
    [property: JsonPropertyName("labels")] IReadOnlyDictionary<string, string>? Labels);

/// <summary>Reading the corpus and the 70/30 split.</summary>
public static class Corpus
{
    /// <summary>Loads all entries of a JSONL file; lines that are no JSON object are skipped.</summary>
    public static IReadOnlyList<CorpusEntry> Load(string path) =>
        File.ReadLines(path)
            .Where(l => l.TrimStart().StartsWith('{'))
            .Select(l => JsonSerializer.Deserialize<CorpusEntry>(l)!)
            .ToList();

    /// <summary>Development set: first byte of the SHA-256 of the request modulo 10 below 7 (same rule as the length corpus).</summary>
    public static bool InDevelopmentSet(string request) => SHA256.HashData(Encoding.UTF8.GetBytes(request))[0] % 10 < 7;

    /// <summary><c>dev</c> or <c>holdout</c>.</summary>
    public static string Split(this CorpusEntry entry) => InDevelopmentSet(entry.Request) ? "dev" : "holdout";
}
