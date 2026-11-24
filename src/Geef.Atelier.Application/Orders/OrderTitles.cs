using System.Text;

namespace Geef.Atelier.Application.Orders;

/// <summary>Short titles and file name parts derived from the request text of an order.</summary>
public static class OrderTitles
{
    private const int MaxTitleChars = 80;
    private const int MaxSlugChars = 50;

    /// <summary>Returns the first non-empty line of the request, shortened to 80 characters at a word boundary.</summary>
    public static string ToOrderTitle(this string? requestText)
    {
        var line = (requestText ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0) ?? string.Empty;
        line = string.Join(' ', line.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (line.Length == 0)
            return "Untitled";
        if (line.Length <= MaxTitleChars)
            return line;

        var cut = line.LastIndexOf(' ', MaxTitleChars - 1);
        if (cut < MaxTitleChars / 2)
            cut = MaxTitleChars - 1;
        if (char.IsHighSurrogate(line[cut - 1]))
            cut--;
        return line[..cut].TrimEnd(' ', ',', ';', ':', '-') + "…";
    }

    /// <summary>Returns a lower-case file name part of letters, digits and dashes, at most 50 characters; <c>text</c> when nothing is left.</summary>
    public static string ToFileSlug(this string? title)
    {
        var builder = new StringBuilder();
        foreach (var c in (title ?? string.Empty).Normalize(NormalizationForm.FormC))
        {
            if (char.IsLetterOrDigit(c))
                builder.Append(char.ToLowerInvariant(c));
            else if (builder.Length > 0 && builder[^1] != '-')
                builder.Append('-');
            if (builder.Length >= MaxSlugChars)
                break;
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? "text" : slug;
    }
}
