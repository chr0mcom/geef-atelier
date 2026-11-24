using System.Globalization;
using System.Text;

namespace Geef.Atelier.Core.Domain.Sources;

/// <summary>A selection of pages such as <c>1-40,55</c>: inclusive, 1-based ranges.</summary>
public sealed record PageSelection(IReadOnlyList<(int From, int To)> Ranges)
{
    /// <summary>
    /// Parses <paramref name="text"/> against a document with <paramref name="pageCount"/> pages. Returns false with a
    /// plain-English <paramref name="error"/> for malformed or out-of-range input. Blank text means all pages: the
    /// result is true with <paramref name="selection"/> = <c>null</c>. Ranges are sorted and merged.
    /// </summary>
    public static bool TryParse(string? text, int pageCount, out PageSelection? selection, out string? error)
    {
        selection = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var ranges = new List<(int From, int To)>();
        foreach (var raw in text.Split([',', ';'], StringSplitOptions.TrimEntries))
        {
            if (raw.Length == 0)
                continue;

            var parts = raw.Replace('–', '-').Replace('—', '-').Split('-', StringSplitOptions.TrimEntries);
            if (parts.Length is < 1 or > 2 || !TryPage(parts[0], out var from) || (parts.Length == 2 && !TryPage(parts[1], out _)))
            {
                error = "Use page numbers like 1-40, 55.";
                return false;
            }

            var to = parts.Length == 2 ? int.Parse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture) : from;
            if (from < 1 || to < 1)
            {
                error = "Page numbers start at 1.";
                return false;
            }

            if (to < from)
            {
                error = "A range goes from a lower to a higher page, for example 3-7.";
                return false;
            }

            if (to > pageCount)
            {
                error = string.Create(CultureInfo.InvariantCulture,
                    $"Page {to} does not exist; the file has {pageCount} {(pageCount == 1 ? "page" : "pages")}.");
                return false;
            }

            ranges.Add((from, to));
        }

        if (ranges.Count == 0)
        {
            error = "Use page numbers like 1-40, 55.";
            return false;
        }

        selection = new PageSelection(Merge(ranges));
        return true;
    }

    /// <summary>True when <paramref name="page"/> lies in one of the ranges.</summary>
    public bool Contains(int page) => Ranges.Any(r => page >= r.From && page <= r.To);

    /// <summary>Number of selected pages.</summary>
    public int PageCount => Ranges.Sum(r => r.To - r.From + 1);

    /// <summary>Keeps only the selected pages of extracted text, including their page markers.</summary>
    public string ApplyTo(string extractedText)
    {
        ArgumentNullException.ThrowIfNull(extractedText);

        var builder = new StringBuilder();
        foreach (var (page, text) in PageMarkers.SplitPages(extractedText).Where(p => Contains(p.Page)))
        {
            if (builder.Length > 0)
                builder.Append('\n');
            builder.Append(PageMarkers.For(page));
            if (text.Length > 0)
                builder.Append('\n').Append(text);
        }

        return builder.ToString();
    }

    /// <summary>Returns the canonical form, e.g. <c>1-40,55</c>.</summary>
    public override string ToString() =>
        string.Join(',', Ranges.Select(r => r.From == r.To
            ? r.From.ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{r.From}-{r.To}")));

    /// <summary>Two selections are equal when they cover the same ranges.</summary>
    public bool Equals(PageSelection? other) => other is not null && Ranges.SequenceEqual(other.Ranges);

    /// <inheritdoc/>
    public override int GetHashCode() => ToString().GetHashCode(StringComparison.Ordinal);

    private static bool TryPage(string value, out int page) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out page);

    private static List<(int From, int To)> Merge(List<(int From, int To)> ranges)
    {
        var merged = new List<(int From, int To)>();
        foreach (var range in ranges.OrderBy(r => r.From).ThenBy(r => r.To))
        {
            if (merged.Count > 0 && range.From <= merged[^1].To + 1)
                merged[^1] = (merged[^1].From, Math.Max(merged[^1].To, range.To));
            else
                merged.Add(range);
        }

        return merged;
    }
}
