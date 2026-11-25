using System.Text.RegularExpressions;

namespace Geef.Atelier.Core.Domain.Intake;

/// <summary>Words a question to the person must not contain, and answer options that are no real answer.</summary>
public static class JargonBlocklist
{
    /// <summary>Blocked terms, matched on word boundaries without regard to case; plural endings (s, n, en, es) count as the same word.</summary>
    public static IReadOnlyList<string> Terms { get; } =
    [
        "prompt", "token", "llm", "ki-modell", "ai model", "model", "modell", "crew", "run", "grounding", "iteration",
        "convergence", "konvergenz", "briefing", "reviewer", "pipeline", "template", "vorlage", "executor", "json", "markdown",
        "schema", "parameter", "tone of voice", "tonalität", "zielgruppe", "target audience", "persona", "call to action", "cta",
        "register", "stakeholder", "use case", "deliverable", "seo",
    ];

    /// <summary>Options that are added automatically ("You decide", free text) or are no real answer.</summary>
    public static IReadOnlyList<string> FillerOptions { get; } =
    [
        "other", "others", "something else", "you decide", "decide for me", "doesn't matter", "does not matter", "no preference",
        "not sure", "don't know", "egal", "sonstiges", "anderes", "etwas anderes", "du entscheidest", "entscheide du",
        "keine angabe", "weiß nicht", "weiss nicht",
    ];

    private static readonly Regex TermPattern = new(
        $@"(?<![\p{{L}}\p{{N}}])(?:{string.Join('|', Terms.OrderByDescending(t => t.Length).Select(Escape))})(?:s|n|en|es)?(?![\p{{L}}\p{{N}}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TrailingNote = new(@"\s*\([^()]*\)\s*$", RegexOptions.CultureInvariant);

    private static readonly HashSet<string> Fillers = new(FillerOptions.Select(Normalize), StringComparer.Ordinal);

    /// <summary>True when <paramref name="text"/> contains a term of <see cref="Terms"/> on word boundaries, ignoring case.</summary>
    public static bool Contains(string text) => !string.IsNullOrEmpty(text) && TermPattern.IsMatch(text);

    /// <summary>
    /// True when <paramref name="option"/> is one of <see cref="FillerOptions"/>, ignoring case, surrounding punctuation, a
    /// trailing note in brackets ("Other (please say)") and the apostrophe form.
    /// </summary>
    public static bool IsFillerOption(string option) => !string.IsNullOrWhiteSpace(option) && Fillers.Contains(Normalize(option));

    private static string Escape(string term) => Regex.Escape(term).Replace(@"\ ", @"\s+", StringComparison.Ordinal).Replace("-", @"[-\s]?", StringComparison.Ordinal);

    private static string Normalize(string option)
    {
        var text = TrailingNote.Replace(option.Trim(), string.Empty);
        text = text.Replace('’', '\'').Replace('‘', '\'');
        text = text.Trim().Trim('.', ',', ':', ';', '!', '?', '…', '-', '–', '—', '"', '„', '“', '”', '\'').Trim();
        return Regex.Replace(text, @"\s+", " ").ToLowerInvariant();
    }
}
