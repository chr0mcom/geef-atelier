using System.Globalization;
using System.Text.RegularExpressions;

namespace Geef.Atelier.Core.Domain.Sources;

/// <summary>Page markers in extracted PDF text: one marker per page, on its own line, before the page text.</summary>
public static partial class PageMarkers
{
    /// <summary>Composite format of a marker; the argument is the 1-based page number.</summary>
    public const string Format = "[[page {0}]]";

    /// <summary>Returns the marker of page <paramref name="page"/>.</summary>
    public static string For(int page) =>
        string.Format(CultureInfo.InvariantCulture, Format, page);

    /// <summary>
    /// Splits extracted text at its markers into pages; text without markers is one page (page 1). Text before the
    /// first marker belongs to the first page.
    /// </summary>
    public static IReadOnlyList<(int Page, string Text)> SplitPages(string extractedText)
    {
        ArgumentNullException.ThrowIfNull(extractedText);

        var preamble = new List<string>();
        var pages = new List<(int Page, List<string> Lines)>();

        foreach (var line in SplitLines(extractedText))
        {
            if (TryParseMarker(line, out var page))
                pages.Add((page, []));
            else if (pages.Count == 0)
                preamble.Add(line);
            else
                pages[^1].Lines.Add(line);
        }

        if (pages.Count == 0)
            return [(1, extractedText)];

        if (preamble.Any(l => l.Trim().Length > 0))
            pages[0].Lines.InsertRange(0, preamble);

        return pages.Select(p => (p.Page, string.Join('\n', p.Lines).Trim('\n'))).ToList();
    }

    /// <summary>Removes all marker lines from extracted text.</summary>
    public static string StripMarkers(string extractedText)
    {
        ArgumentNullException.ThrowIfNull(extractedText);
        if (!extractedText.Contains("[[page ", StringComparison.Ordinal))
            return extractedText;

        return string.Join('\n', SplitLines(extractedText).Where(line => !TryParseMarker(line, out _)));
    }

    /// <summary>True when <paramref name="line"/> is exactly a page marker; returns its page number.</summary>
    public static bool TryParseMarker(string line, out int page)
    {
        page = 0;
        var match = MarkerRegex().Match(line);
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out page);
    }

    private static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    [GeneratedRegex(@"^\[\[page (\d{1,6})\]\]$", RegexOptions.CultureInvariant)]
    private static partial Regex MarkerRegex();
}
