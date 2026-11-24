using System.Text;
using Geef.Atelier.Core.Domain.Sources;

namespace Geef.Atelier.Infrastructure.Sources.Extraction;

/// <summary>Extracted text as it is stored, with the counts the receipt and the budget use.</summary>
/// <param name="Text">Text with <c>\n</c> line ends only and no line longer than the limit.</param>
/// <param name="Wrapped">True when at least one line was broken.</param>
/// <param name="CharCount">Characters without page-marker lines, equal to <c>PageMarkers.StripMarkers(Text).Length</c>.</param>
/// <param name="WordCount">Words without page-marker lines, equal to <c>WordCounter.Count(PageMarkers.StripMarkers(Text))</c>.</param>
internal readonly record struct NormalizedSourceText(string Text, bool Wrapped, int CharCount, int WordCount);

/// <summary>
/// Prepares extracted text for storage in one pass: line ends become <c>\n</c> (so a lone <c>\r</c> starts a line before
/// anything else looks at the lines), lines that look like page markers are neutralised when asked for, long lines are
/// broken (every broken piece is neutralised as well), and characters and words are counted without page-marker lines.
/// Text that needs none of this is returned as it is, without a copy.
/// </summary>
internal static class SourceTextNormalizer
{
    private const string MarkerStart = "[[page ";

    /// <summary>
    /// Normalises <paramref name="text"/>; <paramref name="defuseMarkerLines"/> neutralises every line that looks like a page
    /// marker (all files except PDFs, whose markers are real).
    /// </summary>
    public static NormalizedSourceText NormalizeForStorage(this string text, bool defuseMarkerLines, int maxLineChars)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLineChars, 20);

        var rewrite = text.Contains('\r') || (defuseMarkerLines && text.Contains("[[", StringComparison.Ordinal)) || HasLongLine(text, maxLineChars);
        var builder = rewrite ? new StringBuilder(text.Length + text.Length / maxLineChars + 16) : null;
        var counter = new Counter();
        var wrapped = false;
        var position = 0;
        while (true)
        {
            var rest = text.AsSpan(position);
            var length = rest.IndexOfAny('\r', '\n');
            var line = length < 0 ? rest : rest[..length];
            if (builder is null)
                counter.Add(line);
            else
                wrapped |= Emit(builder, line, defuseMarkerLines, maxLineChars, ref counter);

            if (length < 0)
                break;
            position += length + 1;
            if (text[position - 1] == '\r' && position < text.Length && text[position] == '\n')
                position++;
            builder?.Append('\n');
        }

        return new NormalizedSourceText(builder?.ToString() ?? text, wrapped, counter.Chars, counter.Words);
    }

    /// <summary>
    /// The first <paramref name="maxChars"/> characters of <paramref name="extractedText"/> without page-marker lines and
    /// surrounding white space, with <c>…</c> when it was cut; reads only as much of the text as it needs.
    /// </summary>
    public static string PreviewWithoutPageMarkers(this string? extractedText, int maxChars)
    {
        if (string.IsNullOrEmpty(extractedText))
            return string.Empty;
        if (extractedText.Contains('\r'))
            return Cut(PageMarkers.StripMarkers(extractedText).Trim(), maxChars);

        var builder = new StringBuilder(Math.Min(extractedText.Length, maxChars + 256));
        var nextCheck = maxChars + 1;
        var first = true;
        var position = 0;
        while (position <= extractedText.Length)
        {
            var rest = extractedText.AsSpan(position);
            var length = rest.IndexOf('\n');
            var line = length < 0 ? rest : rest[..length];
            position += line.Length + 1;
            if (IsMarker(line))
                continue;

            if (!first)
                builder.Append('\n');
            first = false;
            builder.Append(line);
            if (builder.Length < nextCheck)
                continue;

            var collected = builder.ToString().Trim();
            if (collected.Length > maxChars)
                return Cut(collected, maxChars);
            nextCheck = builder.Length * 2;
        }

        return Cut(builder.ToString().Trim(), maxChars);
    }

    private static string Cut(string plain, int maxChars)
    {
        if (plain.Length <= maxChars)
            return plain;
        var cut = maxChars;
        if (char.IsHighSurrogate(plain[cut - 1]))
            cut--;
        return plain[..cut].TrimEnd() + "…";
    }

    private static bool HasLongLine(string text, int maxLineChars)
    {
        var rest = text.AsSpan();
        while (rest.Length > maxLineChars)
        {
            var end = rest.IndexOfAny('\r', '\n');
            if (end < 0)
                return true;
            if (end > maxLineChars)
                return true;
            rest = rest[(end + 1)..];
        }

        return false;
    }

    private static bool Emit(StringBuilder builder, ReadOnlySpan<char> line, bool defuseMarkerLines, int maxLineChars, ref Counter counter)
    {
        if (defuseMarkerLines && line.LooksLikePageMarker())
        {
            var defused = line.ToString().DefusePageMarker();
            return EmitWrapped(builder, defused.AsSpan(), maxLineChars, ref counter);
        }

        return EmitWrapped(builder, line, maxLineChars, ref counter);
    }

    private static bool EmitWrapped(StringBuilder builder, ReadOnlySpan<char> line, int maxLineChars, ref Counter counter)
    {
        if (line.Length <= maxLineChars)
        {
            builder.Append(line);
            counter.Add(line);
            return false;
        }

        var rest = line;
        while (rest.Length > maxLineChars)
        {
            var window = rest[..(maxLineChars + 1)];
            var cut = window.LastIndexOf(' ');
            ReadOnlySpan<char> piece;
            if (cut < maxLineChars / 2)
            {
                cut = maxLineChars;
                if (char.IsHighSurrogate(rest[cut - 1]))
                    cut--;
                piece = rest[..cut];
                rest = rest[cut..];
            }
            else
            {
                piece = rest[..cut].TrimEnd();
                rest = rest[(cut + 1)..];
            }

            AppendPiece(builder, piece, ref counter);
            builder.Append('\n');
        }

        AppendPiece(builder, rest, ref counter);
        return true;
    }

    private static void AppendPiece(StringBuilder builder, ReadOnlySpan<char> piece, ref Counter counter)
    {
        if (piece.LooksLikePageMarker())
        {
            var defused = piece.ToString().DefusePageMarker();
            builder.Append(defused);
            counter.Add(defused);
            return;
        }

        builder.Append(piece);
        counter.Add(piece);
    }

    private static bool IsMarker(ReadOnlySpan<char> line) =>
        line.StartsWith(MarkerStart, StringComparison.Ordinal) && PageMarkers.TryParseMarker(line.ToString(), out _);

    private struct Counter
    {
        private long _chars;
        private int _lines;
        private int _words;

        public readonly int Chars => (int)Math.Min(int.MaxValue, _chars + Math.Max(0, _lines - 1));

        public readonly int Words => _words;

        public void Add(ReadOnlySpan<char> line)
        {
            if (IsMarker(line))
                return;
            _lines++;
            _chars += line.Length;
            var inWord = false;
            foreach (var c in line)
            {
                var space = char.IsWhiteSpace(c);
                if (!space && !inWord)
                    _words++;
                inWord = !space;
            }
        }
    }
}
