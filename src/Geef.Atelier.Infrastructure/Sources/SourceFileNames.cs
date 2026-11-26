using System.Globalization;
using System.Text;

namespace Geef.Atelier.Infrastructure.Sources;

/// <summary>Cleans file names given by the browser or an MCP client for display.</summary>
internal static class SourceFileNames
{
    private const int MaxLength = 255;

    /// <summary>
    /// Keeps only the last path segment, drops control, format and reserved characters, collapses spaces and keeps at most 255
    /// characters with the extension. Characters are judged as whole Unicode scalars, so invisible ones beyond the first plane (tag
    /// characters from U+E0000) and direction controls go as well; letters with accents and emoji stay, and so do the joiners
    /// U+200C and U+200D that join emoji and some scripts.
    /// </summary>
    public static string Sanitize(string? fileName)
    {
        var name = (fileName ?? string.Empty).Replace('\\', '/');
        var slash = name.LastIndexOf('/');
        if (slash >= 0)
            name = name[(slash + 1)..];

        var wellFormed = new StringBuilder(name.Length);
        foreach (var rune in name.EnumerateRunes())
        {
            if (!IsDropped(rune))
                wellFormed.Append(rune.ToString());
        }

        var builder = new StringBuilder(wellFormed.Length);
        var lastWasSpace = false;
        foreach (var rune in wellFormed.ToString().Normalize(NormalizationForm.FormC).EnumerateRunes())
        {
            if (IsDropped(rune))
                continue;
            var space = Rune.IsWhiteSpace(rune);
            if (space && lastWasSpace)
                continue;
            if (space)
                builder.Append(' ');
            else
                builder.Append(rune.ToString());
            lastWasSpace = space;
        }

        var cleaned = builder.ToString().Trim().Trim('.').Trim();
        if (cleaned.Length == 0)
            return "file";
        if (cleaned.Length <= MaxLength)
            return cleaned;

        var extension = Path.GetExtension(cleaned);
        if (extension.Length is 0 or > 10)
            return Cut(cleaned, MaxLength);
        return Cut(cleaned, MaxLength - extension.Length) + extension;
    }

    private static string Cut(string text, int length) =>
        char.IsHighSurrogate(text[length - 1]) ? text[..(length - 1)] : text[..length];

    private static bool IsDropped(Rune rune)
    {
        if (rune == Rune.ReplacementChar || rune.Value is '<' or '>' or ':' or '"' or '|' or '?' or '*')
            return true;
        if (rune.Value is 0x200C or 0x200D)
            return false;
        return Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate;
    }
}
