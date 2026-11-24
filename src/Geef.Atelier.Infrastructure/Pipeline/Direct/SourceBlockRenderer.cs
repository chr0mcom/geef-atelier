using System.Globalization;
using System.Text.RegularExpressions;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Sources;

namespace Geef.Atelier.Infrastructure.Pipeline.Direct;

/// <summary>
/// Renders the files of an order into the background document of the writer: every file inside a
/// <c>&lt;source&gt;</c> tag with its page markers. Above <see cref="SourceOptions.FullTextMaxChars"/> characters a
/// <c>&lt;source-map&gt;</c> precedes the files; its line numbers are exact (1-based) for the rendered document,
/// because the cli-proxy writes the document unchanged to <c>context.md</c>. Tag-like text inside files and file names
/// is defused so that a file cannot close its own tag.
/// </summary>
internal static partial class SourceBlockRenderer
{
    /// <summary>Up to this many pages per file every page is listed in the map; above it only every fifth.</summary>
    public const int MapAllPagesUpTo = 100;

    /// <summary>Step of the listed pages for long files (first and last page are always listed).</summary>
    public const int MapPageStep = 5;

    /// <summary>First line inside the map.</summary>
    public const string MapHeader =
        "Map of this document: every file with the lines it takes up, and the line where each listed page starts. Line 1 is the first line of this document.";

    [GeneratedRegex(@"^\[\[page (\d+)\]\]$", RegexOptions.CultureInvariant)]
    private static partial Regex PageMarkerLine();

    [GeneratedRegex("<(/?)(source)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TagStart();

    private sealed record RenderedSource(PromptSource Source, int FirstLine, int LastLine, IReadOnlyList<(int Page, int Line)> Pages);

    /// <summary>Renders <paramref name="sources"/> in their given order; the mode follows from the summed characters.</summary>
    public static OrderSourceBundle Render(IReadOnlyList<PromptSource> sources, SourceOptions options)
    {
        if (sources.Count == 0)
            return OrderSourceBundle.Empty;

        var total = sources.Sum(s => s.Characters);
        var mode = total <= options.FullTextMaxChars ? SourceUsageMode.FullText : SourceUsageMode.FullTextWithMap;

        var body = new List<string> { $"<sources count=\"{sources.Count}\" characters=\"{total}\">" };
        var rendered = new List<RenderedSource>(sources.Count);
        foreach (var source in sources)
        {
            body.Add(OpenTag(source));
            var first = body.Count;
            var pages = new List<(int Page, int Line)>();
            foreach (var line in ContentLines(source.Text))
            {
                var marker = PageMarkerLine().Match(line);
                if (marker.Success && int.TryParse(marker.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var page))
                    pages.Add((page, body.Count));
                body.Add(Defuse(line));
            }
            var last = Math.Max(first, body.Count - 1);
            if (body.Count == first)
                first = last = first - 1;
            body.Add("</source>");
            rendered.Add(new RenderedSource(source, first, last, pages));
        }
        body.Add("</sources>");

        if (mode == SourceUsageMode.FullText)
            return new OrderSourceBundle(sources, mode, string.Join('\n', body), total);

        var listed = rendered.Select(r => ListedPages(r.Pages)).ToList();
        var mapLineCount = 3 + rendered.Count + listed.Sum(l => l.Count);
        var map = new List<string>(mapLineCount) { "<source-map>", MapHeader };
        for (var i = 0; i < rendered.Count; i++)
        {
            var r = rendered[i];
            map.Add($"{r.Source.Id} \"{AttributeValue(r.Source.FileName)}\"{PagesNote(r.Source)} — lines {mapLineCount + r.FirstLine + 1}–{mapLineCount + r.LastLine + 1}");
            foreach (var (page, line) in listed[i])
                map.Add($"  page {page}: line {mapLineCount + line + 1}");
        }
        map.Add("</source-map>");

        return new OrderSourceBundle(sources, mode, string.Join('\n', map.Concat(body)), total);
    }

    /// <summary>Defuses tag-like text (<c>&lt;source</c>, <c>&lt;/sources</c>, <c>&lt;source-map</c> …) by replacing its angle bracket.</summary>
    public static string Defuse(string text) => TagStart().Replace(text, "‹$1$2");

    private static string OpenTag(PromptSource source)
    {
        var pages = source.PageCount is { } count ? $" pages=\"{count}\"" : string.Empty;
        var selected = string.IsNullOrWhiteSpace(source.PagesIncluded) ? string.Empty : $" selected=\"{AttributeValue(source.PagesIncluded)}\"";
        return $"<source id=\"{source.Id}\" file=\"{AttributeValue(source.FileName)}\" type=\"{TypeName(source.Kind)}\"{pages}{selected} characters=\"{source.Characters}\">";
    }

    private static string PagesNote(PromptSource source)
    {
        if (source.PageCount is not { } count)
            return string.Empty;
        var selected = string.IsNullOrWhiteSpace(source.PagesIncluded) ? string.Empty : $", selected {source.PagesIncluded}";
        return $" — {count} pages{selected}";
    }

    private static IReadOnlyList<(int Page, int Line)> ListedPages(IReadOnlyList<(int Page, int Line)> pages)
    {
        if (pages.Count <= MapAllPagesUpTo)
            return pages;
        var first = pages[0].Page;
        var last = pages[^1].Page;
        return pages.Where(p => p.Page == first || p.Page == last || p.Page % MapPageStep == 0).ToList();
    }

    private static IEnumerable<string> ContentLines(string text)
    {
        var normalized = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n');
        return normalized.Length == 0 ? [] : normalized.Split('\n');
    }

    private static string AttributeValue(string value) =>
        Defuse(value.Replace('"', '\'').Replace('\n', ' ').Replace('\r', ' '));

    private static string TypeName(SourceKind kind) => kind switch
    {
        SourceKind.Pdf => "pdf",
        SourceKind.Docx => "docx",
        SourceKind.Markdown => "markdown",
        _ => "text",
    };
}
