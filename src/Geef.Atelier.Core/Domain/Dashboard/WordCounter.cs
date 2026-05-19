using System.Text.RegularExpressions;

namespace Geef.Atelier.Core.Domain.Dashboard;

/// <summary>Counts whitespace-delimited words in a text string.</summary>
public static partial class WordCounter
{
    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();

    /// <summary>Returns the word count of <paramref name="text"/>, or 0 for null/empty/whitespace-only input.</summary>
    public static int Count(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        var trimmed = text.Trim();
        return WhitespaceRegex().Split(trimmed).Length;
    }
}
