using System.Text.RegularExpressions;

namespace Geef.Atelier.Core.Domain.Checks;

/// <summary>
/// Guesses whether a text is German or English from frequent function words. Words that exist in both languages
/// (such as <c>in</c>, <c>an</c>, <c>was</c>, <c>will</c>) are ignored. The guess is only returned when one language
/// clearly dominates; mixed texts, short texts and other languages give <c>null</c>.
/// </summary>
public static partial class TextLanguageGuess
{
    /// <summary>Fewest function words of the winning language before a guess is made.</summary>
    public const int MinimumMarkers = 8;

    /// <summary>Share the winning language must reach among all counted function words.</summary>
    public const double MinimumShare = 0.8;

    private static readonly HashSet<string> GermanMarkers = new(StringComparer.Ordinal)
    {
        "der", "die", "das", "und", "ist", "nicht", "ein", "eine", "einen", "einem", "einer", "mit", "für", "auf",
        "sich", "den", "dem", "des", "zu", "von", "im", "es", "ich", "sie", "wir", "auch", "wird", "werden", "oder",
        "aber", "als", "wie", "bei", "nach", "noch", "nur", "dass", "sind", "haben", "kann", "über", "bitte", "ihr",
        "ihre", "ihnen", "unser", "unsere", "zum", "zur", "vom", "beim", "wenn", "weil", "durch", "gegen", "ohne",
        "sehr", "mehr", "schon", "jetzt", "hier", "diese", "dieser", "dieses", "kein", "keine", "können", "müssen",
        "soll", "sollte", "wurde", "wurden", "gibt", "uns", "euch", "mich", "mir", "dich", "dir",
    };

    private static readonly HashSet<string> EnglishMarkers = new(StringComparer.Ordinal)
    {
        "the", "and", "is", "are", "not", "a", "with", "for", "on", "to", "of", "it", "this", "that", "be", "you",
        "we", "or", "but", "at", "by", "from", "have", "has", "can", "please", "your", "our", "they", "their",
        "there", "which", "would", "should", "could", "been", "were", "what", "when", "if", "because", "about",
        "into", "than", "then", "these", "those", "them", "its", "my", "me", "us", "do", "does", "did", "no", "more",
        "very", "just", "only", "all", "any", "each", "how", "who", "whom", "why", "where", "here",
    };

    [GeneratedRegex(@"\p{L}+", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"https?://\S+", RegexOptions.CultureInvariant)]
    private static partial Regex UrlPattern();

    /// <summary>Returns <c>de</c> or <c>en</c> when the guess is certain, otherwise <c>null</c>.</summary>
    public static string? Guess(string text)
    {
        var (german, english) = Count(text);
        var total = german + english;
        if (total == 0)
            return null;
        if (german >= MinimumMarkers && german >= total * MinimumShare)
            return "de";
        if (english >= MinimumMarkers && english >= total * MinimumShare)
            return "en";
        return null;
    }

    /// <summary>Counts the German and the English function words of <paramref name="text"/>.</summary>
    public static (int German, int English) Count(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (0, 0);

        var german = 0;
        var english = 0;
        foreach (Match match in WordPattern().Matches(UrlPattern().Replace(text, " ")))
        {
            var word = match.Value.ToLowerInvariant();
            if (GermanMarkers.Contains(word))
                german++;
            else if (EnglishMarkers.Contains(word))
                english++;
        }

        return (german, english);
    }
}
