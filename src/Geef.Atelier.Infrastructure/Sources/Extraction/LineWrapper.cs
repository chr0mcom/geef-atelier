namespace Geef.Atelier.Infrastructure.Sources.Extraction;

/// <summary>
/// Breaks lines that are longer than a limit, at the last space before the limit when there is one. A piece of a broken
/// line that looks like a page marker is neutralised, so wrapping never creates a page break. Line ends become <c>\n</c>.
/// </summary>
internal static class LineWrapper
{
    /// <summary>Returns <paramref name="text"/> with no line longer than <paramref name="maxLineChars"/>; <paramref name="wrapped"/> tells whether a line was broken.</summary>
    public static string Wrap(string text, int maxLineChars, out bool wrapped)
    {
        var result = text.NormalizeForStorage(defuseMarkerLines: false, maxLineChars);
        wrapped = result.Wrapped;
        return result.Text;
    }
}
