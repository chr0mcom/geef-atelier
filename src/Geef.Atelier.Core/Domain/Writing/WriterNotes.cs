using System.Text.Json;
using System.Text.RegularExpressions;
using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Core.Domain.Writing;

/// <summary>Result of reading the block the writer appends after the text.</summary>
public enum WriterNotesStatus { Off = 0, Ok = 1, Absent = 2, Unreadable = 3, CarriedOver = 4 }

/// <summary>A passage the writer says it copied word for word from the file <see cref="SourceId"/> (<c>S1…</c>, <c>S0</c> for the text to revise).</summary>
public sealed record WriterQuote(string SourceId, string Quote);

/// <summary>
/// What the writer reported next to the text: own assumptions, open points and passages quoted from the files.
/// <see cref="Hidden"/> counts assumptions and open points that are not shown because they hold a link, an address, or account
/// or access details the request does not treat; <see cref="HiddenQuotes"/> the same for quotes. Neither ever disappears silently.
/// </summary>
public sealed record WriterNotes(IReadOnlyList<string> Assumptions, IReadOnlyList<string> OpenPoints, IReadOnlyList<WriterQuote> Quotes,
    int Hidden = 0, int HiddenQuotes = 0)
{
    /// <summary>No notes at all.</summary>
    public static WriterNotes Empty { get; } = new([], [], []);
}

/// <summary>The writer's answer split into the text for the person and the notes for the system.</summary>
public sealed record WriterNotesSplit(string Text, WriterNotes Notes, WriterNotesStatus Status);

/// <summary>
/// The block <c>&lt;!-- atelier-notes {JSON} --&gt;</c> at the end of the writer's answer. <see cref="Split"/> removes everything
/// from the last start marker at the beginning of a line, even when the JSON is broken; <see cref="Defuse"/> keeps a file or a
/// text to revise from ending the text early.
/// </summary>
public static partial class WriterNotesFormat
{
    public const string StartMarker = "<!-- atelier-notes";
    public const string EndMarker = "-->";
    public const string DefusedStartMarker = "<!-- atelier‑notes";
    public const int MaxItems = 10;
    public const int MaxItemChars = 300;
    public const int MaxQuotes = 20;
    public const int MaxQuoteChars = 400;

    private const string AssumptionsKey = "assumptions";
    private const string OpenPointsKey = "open_points";
    private const string QuotesKey = "quotes";

    private static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal) { AssumptionsKey, OpenPointsKey, QuotesKey };

    [GeneratedRegex(@"<!--[ \t]*atelier[-_ ]?notes", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MarkerPattern();

    [GeneratedRegex(@"^```[ \t]*json[ \t]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex JsonFenceOpenPattern();

    [GeneratedRegex(@"^```[A-Za-z]*$", RegexOptions.CultureInvariant)]
    private static partial Regex FenceLinePattern();

    [GeneratedRegex(@"^[ \t]*\{", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex BareObjectStartPattern();

    /// <summary>
    /// Splits <paramref name="raw"/>, the complete answer of the writer, into text and notes. The last start marker at the
    /// beginning of a line counts (any start marker when none stands at the beginning of a line); the text is everything
    /// before it, trimmed at the end, and earlier blocks are cut out of it, because a block left in the text is the worst
    /// failure. Without a marker a final <c>```json</c> block or a final bare JSON object that has only the keys of the
    /// block is taken as the block; otherwise the status is <see cref="WriterNotesStatus.Absent"/> and the text is returned
    /// unchanged.
    /// </summary>
    /// <remarks>Entries with a link, an address, or account or access details are not kept but counted (<see cref="WriterNotes.Hidden"/>,
    /// <see cref="WriterNotes.HiddenQuotes"/>); what the request itself treats (<paramref name="requestText"/>, for example a
    /// transfer or the domain of a named website) is allowed, as in <see cref="IntakeContentGuard.IsUnsafe"/>.</remarks>
    public static WriterNotesSplit Split(string raw, string? requestText = null)
    {
        raw ??= string.Empty;
        var markers = MarkerPattern().Matches(raw).Select(m => m.Index).ToList();
        if (markers.Count > 0)
            return SplitAtMarkers(raw, markers, requestText);

        var fenced = TrySplitFencedJson(raw, requestText);
        if (fenced is not null)
            return fenced;

        var bare = TrySplitBareJson(raw, requestText);
        if (bare is not null)
            return bare;

        return new WriterNotesSplit(raw, WriterNotes.Empty, WriterNotesStatus.Absent);
    }

    /// <summary>Replaces <see cref="StartMarker"/> (in every spelling that <see cref="Split"/> accepts) in <paramref name="text"/> by <see cref="DefusedStartMarker"/>.</summary>
    public static string Defuse(string text) =>
        string.IsNullOrEmpty(text) ? text ?? string.Empty : MarkerPattern().Replace(text, DefusedStartMarker);

    private static WriterNotesSplit SplitAtMarkers(string raw, List<int> markers, string? requestText)
    {
        var atLineStart = markers.Where(i => IsAtLineStart(raw, i)).ToList();
        var candidates = atLineStart.Count > 0 ? atLineStart : markers;
        var last = candidates[^1];

        var head = raw[..last];
        var earlier = markers.Where(i => i < last).ToList();
        if (earlier.Count > 0)
            head = CutEarlierBlocks(head, earlier);

        var text = DropDanglingFence(head).TrimEnd();
        var block = raw[(last + MarkerPattern().Match(raw, last).Length)..];
        var notes = ParseBlock(block, requestText);
        return notes is null
            ? new WriterNotesSplit(text, WriterNotes.Empty, WriterNotesStatus.Unreadable)
            : new WriterNotesSplit(text, notes, WriterNotesStatus.Ok);
    }

    private static bool IsAtLineStart(string raw, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            var c = raw[i];
            if (c == '\n')
                return true;
            if (c is not (' ' or '\t' or '\r'))
                return false;
        }
        return true;
    }

    private static string CutEarlierBlocks(string head, List<int> earlier)
    {
        var segments = new List<string>();
        var position = 0;
        foreach (var start in earlier)
        {
            if (start < position)
                continue;
            segments.Add(head[position..start]);
            var end = head.IndexOf(EndMarker, start + 4, StringComparison.Ordinal);
            position = end < 0 ? head.Length : end + EndMarker.Length;
        }
        segments.Add(head[position..]);
        var kept = segments
            .Select((segment, i) => i == 0 ? segment.TrimEnd() : i == segments.Count - 1 ? segment.TrimStart() : segment.Trim())
            .Select(DropDanglingFence)
            .Where(segment => segment.Trim().Length > 0)
            .ToList();
        return string.Join("\n\n", kept);
    }

    private static string DropDanglingFence(string text)
    {
        var trimmed = text.TrimEnd();
        var lineStart = trimmed.LastIndexOf('\n') + 1;
        var lastLine = trimmed[lineStart..].Trim();
        if (!FenceLinePattern().IsMatch(lastLine))
            return text;
        var fences = trimmed.Split('\n').Count(line => FenceLinePattern().IsMatch(line.Trim()));
        return fences % 2 == 1 ? trimmed[..lineStart].TrimEnd() : text;
    }

    private static WriterNotes? ParseBlock(string block, string? requestText)
    {
        var end = block.LastIndexOf(EndMarker, StringComparison.Ordinal);
        foreach (var candidate in end >= 0 ? new[] { block[..end], block } : [block])
        {
            var open = candidate.IndexOf('{');
            var close = candidate.LastIndexOf('}');
            if (open < 0 || close <= open)
                continue;
            var notes = ParseObject(candidate[open..(close + 1)], requestText, requireOnlyKnownKeys: false);
            if (notes is not null)
                return notes;
        }
        return null;
    }

    private static WriterNotesSplit? TrySplitFencedJson(string raw, string? requestText)
    {
        var trimmed = raw.TrimEnd();
        if (!trimmed.EndsWith("```", StringComparison.Ordinal))
            return null;
        var opens = JsonFenceOpenPattern().Matches(trimmed);
        if (opens.Count == 0)
            return null;
        var open = opens[^1];
        var contentStart = open.Index + open.Length;
        var contentEnd = trimmed.Length - 3;
        if (contentEnd <= contentStart)
            return null;
        var notes = ParseObject(trimmed[contentStart..contentEnd], requestText, requireOnlyKnownKeys: false, requireKnownKey: true);
        return notes is null ? null : new WriterNotesSplit(trimmed[..open.Index].TrimEnd(), notes, WriterNotesStatus.Ok);
    }

    private static WriterNotesSplit? TrySplitBareJson(string raw, string? requestText)
    {
        var trimmed = raw.TrimEnd();
        if (!trimmed.EndsWith('}'))
            return null;
        foreach (Match start in BareObjectStartPattern().Matches(trimmed).Reverse())
        {
            var notes = ParseObject(trimmed[start.Index..], requestText, requireOnlyKnownKeys: true, requireKnownKey: true);
            if (notes is not null)
                return new WriterNotesSplit(trimmed[..start.Index].TrimEnd(), notes, WriterNotesStatus.Ok);
        }
        return null;
    }

    private static WriterNotes? ParseObject(string json, string? requestText, bool requireOnlyKnownKeys, bool requireKnownKey = false)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;
            var keys = root.EnumerateObject().Select(p => p.Name).ToList();
            if (requireKnownKey && !keys.Any(KnownKeys.Contains))
                return null;
            if (requireOnlyKnownKeys && keys.Any(k => !KnownKeys.Contains(k)))
                return null;

            var (assumptions, hiddenAssumptions) = Items(root, AssumptionsKey, requestText);
            var (openPoints, hiddenOpenPoints) = Items(root, OpenPointsKey, requestText);
            var (quotes, hiddenQuotes) = Quotes(root, requestText);
            return new WriterNotes(assumptions, openPoints, quotes, hiddenAssumptions + hiddenOpenPoints, hiddenQuotes);
        }
    }

    private static (IReadOnlyList<string> Items, int Hidden) Items(JsonElement root, string key, string? requestText)
    {
        if (!root.TryGetProperty(key, out var array) || array.ValueKind != JsonValueKind.Array)
            return ([], 0);
        var items = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var hidden = 0;
        foreach (var element in array.EnumerateArray())
        {
            if (items.Count + hidden >= MaxItems)
                break;
            if (element.ValueKind != JsonValueKind.String)
                continue;
            var value = OneLine(element.GetString());
            if (value.Length == 0 || !seen.Add(QuoteVerifier.Normalize(value).ToLowerInvariant()))
                continue;
            if (IntakeContentGuard.IsUnsafe(value, requestText))
            {
                hidden++;
                continue;
            }
            items.Add(Cap(value, MaxItemChars, ellipsis: true));
        }
        return (items, hidden);
    }

    private static (IReadOnlyList<WriterQuote> Quotes, int Hidden) Quotes(JsonElement root, string? requestText)
    {
        if (!root.TryGetProperty(QuotesKey, out var array) || array.ValueKind != JsonValueKind.Array)
            return ([], 0);
        var quotes = new List<WriterQuote>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var hidden = 0;
        foreach (var element in array.EnumerateArray())
        {
            if (quotes.Count + hidden >= MaxQuotes)
                break;
            if (element.ValueKind != JsonValueKind.Object)
                continue;
            var source = element.TryGetProperty("source", out var s) && s.ValueKind == JsonValueKind.String
                ? s.GetString()!.Trim().ToUpperInvariant()
                : string.Empty;
            var quote = element.TryGetProperty("quote", out var q) && q.ValueKind == JsonValueKind.String
                ? (q.GetString() ?? string.Empty).Trim()
                : string.Empty;
            if (source.Length == 0 || quote.Length == 0 || !seen.Add(source + "\n" + QuoteVerifier.Normalize(quote)))
                continue;
            if (IntakeContentGuard.IsUnsafe(quote, requestText))
            {
                hidden++;
                continue;
            }
            quotes.Add(new WriterQuote(source, Cap(quote, MaxQuoteChars, ellipsis: false)));
        }
        return (quotes, hidden);
    }

    private static string OneLine(string? value) =>
        string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Cap(string value, int max, bool ellipsis)
    {
        if (value.Length <= max)
            return value;
        return ellipsis ? value[..(max - 1)].TrimEnd() + "…" : value[..max].TrimEnd();
    }
}
