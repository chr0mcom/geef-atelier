using System.Globalization;
using System.Text;

namespace Geef.Atelier.Core.Domain.Writing;

/// <summary>How a quote was found in a text.</summary>
public enum QuoteMatchKind { NotFound = 0, TooShort = 1, Exact = 2, Normalized = 3, Approximate = 4 }

/// <summary>Where a quote was found; <see cref="Start"/> and <see cref="Length"/> refer to the normalized text.</summary>
public sealed record QuoteMatch(QuoteMatchKind Kind, int Start, int Length, double Similarity)
{
    /// <summary>Found word for word, apart from normalization.</summary>
    public bool IsVerbatim => Kind is QuoteMatchKind.Exact or QuoteMatchKind.Normalized;

    /// <summary>Found word for word or approximately.</summary>
    public bool IsFound => IsVerbatim || Kind == QuoteMatchKind.Approximate;
}

/// <summary>Checks without an AI model whether a quote stands in a text: exactly, after normalization, or approximately.</summary>
public static class QuoteVerifier
{
    public const int MinChars = 12;
    public const int MinWords = 3;
    public const int MinWordsForApproximate = 5;
    public const double ApproximateThreshold = 0.88;

    private const int MaxApproximateStarts = 1500;
    private const int WindowSlack = 2;

    [ThreadStatic]
    private static Prepared? _last;

    /// <summary>NFKC, soft hyphens and format characters removed, line-break hyphenation joined, quotes and dashes unified, white space collapsed; case is kept.</summary>
    public static string Normalize(string text) => Build(text ?? string.Empty, null);

    /// <summary>Looks for <paramref name="quote"/> in <paramref name="haystack"/>: exact, normalized, then (only with <paramref name="allowApproximate"/>) approximate.</summary>
    public static QuoteMatch Find(string haystack, string quote, bool allowApproximate) =>
        FindCore(haystack, quote, allowApproximate, MinChars);

    /// <summary>
    /// Like <see cref="Find"/> without approximation, but quotes from <paramref name="minChars"/> characters on count; for short
    /// passages of the material such as a stated total, which only have to stand there word for word.
    /// </summary>
    public static QuoteMatch FindVerbatim(string haystack, string quote, int minChars) =>
        FindCore(haystack, quote, false, Math.Max(1, minChars));

    /// <summary>
    /// True when <paramref name="quote"/> stands word for word (after <see cref="Normalize"/>, ignoring case) in
    /// <paramref name="haystack"/> as whole words: a quote that starts or ends with a letter or digit must not continue a word
    /// or number there, so "5 Euro pro Kind" is not found in "15 Euro pro Kind". A blank quote is never found.
    /// </summary>
    public static bool ContainsWholeWords(string haystack, string quote) => FindWholeWords(haystack, quote) is not null;

    /// <summary>
    /// Where <paramref name="quote"/> stands in <paramref name="haystack"/> as whole words (see <see cref="ContainsWholeWords"/>):
    /// start and length in the normalized text, or <c>null</c>.
    /// </summary>
    public static (int Start, int Length)? FindWholeWords(string haystack, string quote)
    {
        var needle = Normalize(quote ?? string.Empty).Trim();
        if (needle.Length == 0)
            return null;
        var text = Normalize(haystack ?? string.Empty);
        var position = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        while (position >= 0)
        {
            var end = position + needle.Length;
            var startsClean = position == 0 || !char.IsLetterOrDigit(needle[0]) || !char.IsLetterOrDigit(text[position - 1]);
            var endsClean = end >= text.Length || !char.IsLetterOrDigit(needle[^1]) || !char.IsLetterOrDigit(text[end]);
            if (startsClean && endsClean)
                return (position, needle.Length);
            position = text.IndexOf(needle, position + 1, StringComparison.OrdinalIgnoreCase);
        }
        return null;
    }

    /// <summary>
    /// Like <see cref="Excerpt"/>, but a passage that starts or ends inside a word or number is widened to that whole word or number,
    /// so the excerpt is found again in <paramref name="haystack"/> by <see cref="ContainsWholeWords"/>.
    /// </summary>
    public static string ExcerptWholeWords(string haystack, QuoteMatch match)
    {
        if (haystack is null || match is null || !match.IsFound || match.Length <= 0)
            return string.Empty;
        var prepared = Prepare(haystack);
        if (match.Start < 0 || match.Start >= prepared.Map.Length)
            return string.Empty;
        var end = match.Start + match.Length;
        var originalStart = prepared.Map[match.Start];
        var originalEnd = end < prepared.Map.Length ? prepared.Map[end] : haystack.Length;
        if (originalEnd <= originalStart)
            return string.Empty;
        while (originalStart < originalEnd && char.IsWhiteSpace(haystack[originalStart]))
            originalStart++;
        while (originalEnd > originalStart && char.IsWhiteSpace(haystack[originalEnd - 1]))
            originalEnd--;
        if (originalEnd <= originalStart)
            return string.Empty;
        while (originalStart > 0 && IsWordPart(haystack[originalStart]) && IsWordPart(haystack[originalStart - 1]))
            originalStart--;
        while (originalEnd < haystack.Length && IsWordPart(haystack[originalEnd - 1]) && IsWordPart(haystack[originalEnd]))
            originalEnd++;
        return haystack[originalStart..originalEnd].Trim();
    }

    private static bool IsWordPart(char c) => char.IsLetterOrDigit(c) || c == '\u00AD' || IsCombining(c);

    /// <summary>The passage of the original <paramref name="haystack"/> that <paramref name="match"/> (from <see cref="Find"/> on the same text) points to; empty when nothing was found.</summary>
    public static string Excerpt(string haystack, QuoteMatch match)
    {
        if (haystack is null || match is null || !match.IsFound || match.Length <= 0)
            return string.Empty;
        var prepared = Prepare(haystack);
        if (match.Start < 0 || match.Start >= prepared.Map.Length)
            return string.Empty;
        var end = match.Start + match.Length;
        var originalStart = prepared.Map[match.Start];
        var originalEnd = end < prepared.Map.Length ? prepared.Map[end] : haystack.Length;
        if (originalEnd <= originalStart)
            return string.Empty;
        return haystack[originalStart..originalEnd].Trim();
    }

    private static QuoteMatch FindCore(string haystack, string quote, bool allowApproximate, int minChars)
    {
        haystack ??= string.Empty;
        quote ??= string.Empty;
        var normalizedQuote = Normalize(quote);
        if (normalizedQuote.Length < minChars)
            return new QuoteMatch(QuoteMatchKind.TooShort, 0, 0, 0);

        var prepared = Prepare(haystack);
        var exact = haystack.IndexOf(quote.Trim(), StringComparison.Ordinal);
        if (exact >= 0)
        {
            var start = LowerBound(prepared.Map, exact);
            var end = LowerBound(prepared.Map, exact + quote.Trim().Length);
            return new QuoteMatch(QuoteMatchKind.Exact, start, Math.Max(0, end - start), 1);
        }

        var position = prepared.Normalized.IndexOf(normalizedQuote, StringComparison.Ordinal);
        if (position < 0)
            position = prepared.Normalized.IndexOf(normalizedQuote, StringComparison.OrdinalIgnoreCase);
        if (position >= 0)
            return new QuoteMatch(QuoteMatchKind.Normalized, position, normalizedQuote.Length, 1);

        if (allowApproximate && WordKeys(normalizedQuote).Count >= MinWordsForApproximate)
        {
            var approximate = FindApproximate(prepared, normalizedQuote);
            if (approximate is not null)
                return approximate;
        }

        return new QuoteMatch(QuoteMatchKind.NotFound, 0, 0, 0);
    }

    private static QuoteMatch? FindApproximate(Prepared prepared, string normalizedQuote)
    {
        var words = prepared.Words;
        if (words.Count == 0)
            return null;

        var quoteKeys = WordKeys(normalizedQuote);
        var n = quoteKeys.Count;
        var quoteIds = new int[n];
        for (var i = 0; i < n; i++)
            quoteIds[i] = words.IdOf.TryGetValue(quoteKeys[i], out var id) ? id : -1 - i;

        var anchors = Enumerable.Range(0, n)
            .Where(i => quoteIds[i] >= 0)
            .GroupBy(i => quoteIds[i])
            .Select(g => (Id: g.Key, QuoteIndex: g.First()))
            .OrderBy(a => words.Positions[a.Id].Count)
            .ThenBy(a => a.QuoteIndex)
            .Take(2)
            .ToList();
        if (anchors.Count == 0)
            return null;

        var starts = new SortedSet<int>();
        foreach (var anchor in anchors)
        {
            foreach (var position in words.Positions[anchor.Id])
            {
                for (var delta = -WindowSlack; delta <= WindowSlack; delta++)
                {
                    var start = position - anchor.QuoteIndex + delta;
                    if (start >= 0 && start < words.Count)
                        starts.Add(start);
                }
                if (starts.Count >= MaxApproximateStarts)
                    break;
            }
            if (starts.Count >= MaxApproximateStarts)
                break;
        }

        var best = (Similarity: -1.0, Start: 0, Length: 0);
        var previous = new int[n + WindowSlack + 1];
        var current = new int[n + WindowSlack + 1];
        var lastRow = new int[n + WindowSlack + 1];
        foreach (var start in starts)
        {
            var width = Math.Min(n + WindowSlack, words.Count - start);
            if (width < Math.Max(1, n - WindowSlack))
                continue;
            DistancesAgainstPrefixes(quoteIds, words.Ids, start, width, previous, current, lastRow);
            for (var length = Math.Max(1, n - WindowSlack); length <= width; length++)
            {
                var similarity = 1.0 - (double)lastRow[length] / Math.Max(n, length);
                if (similarity > best.Similarity + 1e-12)
                    best = (similarity, start, length);
            }
        }

        if (best.Similarity + 1e-9 < ApproximateThreshold)
            return null;
        var first = words.Starts[best.Start];
        var last = words.Ends[best.Start + best.Length - 1];
        return new QuoteMatch(QuoteMatchKind.Approximate, first, last - first, Math.Round(best.Similarity, 4));
    }

    private static void DistancesAgainstPrefixes(int[] quote, List<int> haystack, int start, int width, int[] previous, int[] current, int[] lastRow)
    {
        for (var j = 0; j <= width; j++)
            previous[j] = j;
        for (var i = 1; i <= quote.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= width; j++)
            {
                var cost = quote[i - 1] == haystack[start + j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        Array.Copy(previous, lastRow, width + 1);
    }

    private static int LowerBound(int[] map, int value)
    {
        int low = 0, high = map.Length;
        while (low < high)
        {
            var middle = (low + high) >> 1;
            if (map[middle] < value)
                low = middle + 1;
            else
                high = middle;
        }
        return low;
    }

    private static Prepared Prepare(string haystack)
    {
        var last = _last;
        if (last is not null && ReferenceEquals(last.Source, haystack))
            return last;
        var map = new List<int>(haystack.Length);
        var normalized = Build(haystack, map);
        var prepared = new Prepared(haystack, normalized, [.. map]);
        _last = prepared;
        return prepared;
    }

    private static List<string> WordKeys(string normalized)
    {
        var keys = new List<string>();
        foreach (var token in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var key = Key(token);
            if (key.Length > 0)
                keys.Add(key);
        }
        return keys;
    }

    private static string Key(string token)
    {
        int start = 0, end = token.Length;
        while (start < end && !char.IsLetterOrDigit(token[start]))
            start++;
        while (end > start && !char.IsLetterOrDigit(token[end - 1]))
            end--;
        return start >= end ? string.Empty : token[start..end].ToLowerInvariant();
    }

    private static string Build(string text, List<int>? map)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        var pendingIndex = 0;
        var pendingNewline = false;
        var softHyphenAfterLetter = false;
        var index = 0;

        void Emit(char c, int origin)
        {
            if (c == '­')
            {
                if (builder.Length > 0 && !pendingSpace && char.IsLetter(builder[^1]))
                    softHyphenAfterLetter = true;
                return;
            }
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format)
                return;
            if (char.IsWhiteSpace(c))
            {
                if (builder.Length == 0)
                    return;
                if (!pendingSpace)
                {
                    pendingSpace = true;
                    pendingIndex = origin;
                    pendingNewline = false;
                }
                if (c is '\n' or '\r' or '\u2028' or '\u2029')
                    pendingNewline = true;
                return;
            }

            c = Unify(c);
            if (pendingSpace)
            {
                var joinHyphen = pendingNewline && char.IsLower(c) && builder.Length >= 2 && builder[^1] == '-' && char.IsLetter(builder[^2]);
                var joinSoft = pendingNewline && char.IsLower(c) && softHyphenAfterLetter;
                if (joinHyphen)
                {
                    builder.Length--;
                    map?.RemoveAt(map.Count - 1);
                }
                else if (!joinSoft)
                {
                    builder.Append(' ');
                    map?.Add(pendingIndex);
                }
                pendingSpace = false;
            }
            softHyphenAfterLetter = false;
            builder.Append(c);
            map?.Add(origin);
        }

        while (index < text.Length)
        {
            var start = index;
            var first = text[index];
            if (first < 0x80 && (index + 1 >= text.Length || text[index + 1] < '̀'))
            {
                index++;
                Emit(first, start);
                continue;
            }

            index += char.IsHighSurrogate(first) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]) ? 2 : 1;
            while (index < text.Length && IsCombining(text[index]))
                index++;
            var cluster = text.AsSpan(start, index - start);
            string piece;
            try
            {
                piece = cluster.ToString().Normalize(NormalizationForm.FormKC);
            }
            catch (ArgumentException)
            {
                piece = cluster.ToString();
            }
            foreach (var c in piece)
                Emit(c, start);
        }

        return builder.ToString();
    }

    private static bool IsCombining(char c) => CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark
        or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    private static char Unify(char c) => c switch
    {
        '„' or '“' or '”' or '‟' or '«' or '»' or '″' => '"',
        '‚' or '‘' or '’' or '‛' or '‹' or '›' or '′' => '\'',
        '‐' or '‑' or '‒' or '–' or '—' or '―' or '−' => '-',
        _ => c,
    };

    private sealed class Prepared(string source, string normalized, int[] map)
    {
        private WordIndex? _words;

        public string Source { get; } = source;

        public string Normalized { get; } = normalized;

        public int[] Map { get; } = map;

        public WordIndex Words => _words ??= WordIndex.Build(Normalized);
    }

    private sealed class WordIndex
    {
        public Dictionary<string, int> IdOf { get; } = new(StringComparer.Ordinal);

        public List<List<int>> Positions { get; } = [];

        public List<int> Ids { get; } = [];

        public List<int> Starts { get; } = [];

        public List<int> Ends { get; } = [];

        public int Count => Ids.Count;

        public static WordIndex Build(string normalized)
        {
            var index = new WordIndex();
            var position = 0;
            while (position < normalized.Length)
            {
                var end = normalized.IndexOf(' ', position);
                if (end < 0)
                    end = normalized.Length;
                if (end > position)
                {
                    var key = Key(normalized[position..end]);
                    if (key.Length > 0)
                    {
                        if (!index.IdOf.TryGetValue(key, out var id))
                        {
                            id = index.Positions.Count;
                            index.IdOf[key] = id;
                            index.Positions.Add([]);
                        }
                        index.Positions[id].Add(index.Ids.Count);
                        index.Ids.Add(id);
                        index.Starts.Add(position);
                        index.Ends.Add(end);
                    }
                }
                position = end + 1;
            }
            return index;
        }
    }
}
