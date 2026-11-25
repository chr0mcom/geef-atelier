using System.Text.Json;
using Geef.Atelier.Core.Domain.FactCheck;

namespace Geef.Atelier.Infrastructure.Pipeline.Direct.FactCheck;

/// <summary>Findings read from a <c>check_facts</c> answer and how many readable ones were left out (over a cap or with an over-long quote).</summary>
internal sealed record ParsedCheck(IReadOnlyList<ProposedFactFinding> Findings, int Dropped);

/// <summary>Reads the answers of the fact check; caps what the schema deliberately leaves open. <c>null</c> for an answer that cannot be read.</summary>
internal static class FactCheckOutputParser
{
    public const int MaxFindings = 12;
    public const int MaxQuoteChars = 600;
    public const int MaxWhatIsWrongChars = 300;

    private static readonly JsonDocumentOptions Lenient = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    /// <summary>The findings of <see cref="ParseCheck"/> as read by <see cref="ParseCheckCounted"/>.</summary>
    public static IReadOnlyList<ProposedFactFinding>? ParseCheck(string? json) => ParseCheckCounted(json)?.Findings;

    /// <summary>
    /// Findings of a <c>check_facts</c> answer in their given order: at most <see cref="MaxFindings"/> findings about the text and,
    /// counted separately, at most <see cref="MaxFindings"/> source conflicts, so that conflicts in the files never crowd out
    /// passages of the text. A finding with a quote (text, instruction or material) over <see cref="MaxQuoteChars"/> cannot be
    /// verified any more and is left out, like a finding over the caps; both are counted in <see cref="ParsedCheck.Dropped"/>, so
    /// the result never reads as "nothing found". An entry with an unknown kind is not a finding and is not counted.
    /// <c>what_is_wrong</c> is cut to <see cref="MaxWhatIsWrongChars"/> on one line; blank quotes become <c>null</c>.
    /// <c>null</c> when there is no object with a <c>findings</c> list.
    /// </summary>
    public static ParsedCheck? ParseCheckCounted(string? json)
    {
        if (Root(json) is not { } root || !root.TryGetProperty("findings", out var findings) || findings.ValueKind != JsonValueKind.Array)
            return null;

        var kept = new List<ProposedFactFinding>();
        int text = 0, conflicts = 0, dropped = 0;
        foreach (var item in findings.EnumerateArray())
        {
            var (finding, readable) = ReadFinding(item);
            if (finding is null)
            {
                if (readable)
                    dropped++;
                continue;
            }
            var conflict = finding.Kind == FactFindingKind.SourceConflict;
            if ((conflict ? conflicts : text) >= MaxFindings)
            {
                dropped++;
                continue;
            }
            if (conflict)
                conflicts++;
            else
                text++;
            kept.Add(finding);
        }
        return new ParsedCheck(kept, dropped);
    }

    /// <summary>
    /// Results of a <c>recheck_facts</c> answer; unknown ids are left to the caller. An entry without id is dropped, a repeated id
    /// keeps its first answer, an unknown status reads as <see cref="RecheckStatus.Unclear"/>, a quote over
    /// <see cref="MaxQuoteChars"/> or a blank one becomes <c>null</c>. <c>null</c> when there is no object with a <c>results</c> list.
    /// </summary>
    public static IReadOnlyList<RecheckResult>? ParseRecheck(string? json)
    {
        if (Root(json) is not { } root || !root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            return null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var read = new List<RecheckResult>();
        foreach (var item in results.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || Text(item, "id")?.Trim() is not { Length: > 0 } id || !seen.Add(id))
                continue;
            var quote = Text(item, "text_quote");
            read.Add(new RecheckResult(id, Status(Text(item, "status")), quote is null || quote.Length > MaxQuoteChars ? null : quote));
        }
        return read;
    }

    private static (ProposedFactFinding? Finding, bool Readable) ReadFinding(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object || Kind(Text(item, "kind")) is not { } kind)
            return (null, false);

        var textQuote = Text(item, "text_quote");
        var instructionQuote = Text(item, "instruction_quote");
        var materialQuote = Text(item, "material_quote");
        var materialQuote2 = Text(item, "material_quote_2");
        if (new[] { textQuote, instructionQuote, materialQuote, materialQuote2 }.Any(q => q is { Length: > MaxQuoteChars }))
            return (null, true);

        return (new ProposedFactFinding(kind, textQuote, OneLine(Text(item, "what_is_wrong") ?? string.Empty, MaxWhatIsWrongChars),
            instructionQuote, materialQuote, materialQuote2), true);
    }

    private static JsonElement? Root(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        var start = json.IndexOf('{');
        var end = json.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;

        try
        {
            using var document = JsonDocument.Parse(json[start..(end + 1)], Lenient);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static FactFindingKind? Kind(string? kind) => kind?.Trim().ToLowerInvariant() switch
    {
        "unsupported" => FactFindingKind.Unsupported,
        "instruction" => FactFindingKind.Instruction,
        "superseded" => FactFindingKind.Superseded,
        "source_conflict" => FactFindingKind.SourceConflict,
        _ => null,
    };

    private static RecheckStatus Status(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "fixed" => RecheckStatus.Fixed,
        "still_there" => RecheckStatus.StillThere,
        _ => RecheckStatus.Unclear,
    };

    private static string OneLine(string text, int max)
    {
        var line = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (line.Length <= max)
            return line;
        var cut = char.IsHighSurrogate(line[max - 1]) ? max - 1 : max;
        return line[..cut].TrimEnd();
    }
}
