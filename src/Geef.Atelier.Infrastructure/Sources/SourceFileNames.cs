using System.Text;

namespace Geef.Atelier.Infrastructure.Sources;

/// <summary>Cleans file names given by the browser or an MCP client for display.</summary>
internal static class SourceFileNames
{
    private const int MaxLength = 255;

    /// <summary>Keeps only the last path segment, drops control and reserved characters, collapses spaces and keeps at most 255 characters with the extension.</summary>
    public static string Sanitize(string? fileName)
    {
        var name = (fileName ?? string.Empty).Replace('\\', '/');
        var slash = name.LastIndexOf('/');
        if (slash >= 0)
            name = name[(slash + 1)..];

        var builder = new StringBuilder(name.Length);
        var lastWasSpace = false;
        foreach (var c in name.Normalize(NormalizationForm.FormC))
        {
            if (char.IsControl(c) || c is '<' or '>' or ':' or '"' or '|' or '?' or '*' || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format)
                continue;
            var space = char.IsWhiteSpace(c);
            if (space && lastWasSpace)
                continue;
            builder.Append(space ? ' ' : c);
            lastWasSpace = space;
        }

        var cleaned = builder.ToString().Trim().Trim('.').Trim();
        if (cleaned.Length == 0)
            return "file";
        if (cleaned.Length <= MaxLength)
            return cleaned;

        var extension = Path.GetExtension(cleaned);
        if (extension.Length is 0 or > 10)
            return cleaned[..MaxLength];
        return cleaned[..(MaxLength - extension.Length)] + extension;
    }
}
