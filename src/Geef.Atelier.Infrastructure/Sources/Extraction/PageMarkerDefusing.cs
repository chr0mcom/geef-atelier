using System.Text;

namespace Geef.Atelier.Infrastructure.Sources.Extraction;

/// <summary>
/// Neutralises lines of file content that look like page markers (<c>[[page N]]</c>), so only the markers written by
/// the PDF extractor split a source into pages. The check is wider than the marker syntax on purpose.
/// </summary>
internal static class PageMarkerDefusing
{
    /// <summary>True when the trimmed line starts with <c>[[</c>, ends with <c>]]</c> and mentions <c>page</c>.</summary>
    public static bool LooksLikePageMarker(this string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return line.AsSpan().LooksLikePageMarker();
    }

    /// <summary>True when the trimmed line starts with <c>[[</c>, ends with <c>]]</c> and mentions <c>page</c>.</summary>
    public static bool LooksLikePageMarker(this ReadOnlySpan<char> line)
    {
        var trimmed = line.Trim();
        return trimmed.StartsWith("[[", StringComparison.Ordinal)
               && trimmed.EndsWith("]]", StringComparison.Ordinal)
               && trimmed.Contains("page", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns <paramref name="line"/> with its first <c>[[</c> broken up when it looks like a page marker.</summary>
    public static string DefusePageMarker(this string line)
    {
        if (!line.LooksLikePageMarker())
            return line;
        var index = line.IndexOf("[[", StringComparison.Ordinal);
        return string.Concat(line.AsSpan(0, index), "[ [", line.AsSpan(index + 2));
    }

    /// <summary>Returns <paramref name="text"/> with every line that looks like a page marker neutralised.</summary>
    public static string DefusePageMarkerLines(this string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!text.Contains("[[", StringComparison.Ordinal))
            return text;

        var builder = new StringBuilder(text.Length + 16);
        var first = true;
        foreach (var line in text.Split('\n'))
        {
            if (!first)
                builder.Append('\n');
            first = false;
            builder.Append(line.DefusePageMarker());
        }

        return builder.ToString();
    }
}
