namespace Geef.Atelier.Core.Domain.Intake;

/// <summary>Words a question to the person must not contain, and answer options that are no real answer.</summary>
public static class JargonBlocklist
{
    /// <summary>Blocked terms, matched on word boundaries without regard to case.</summary>
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

    /// <summary>True when <paramref name="text"/> contains a term of <see cref="Terms"/> on word boundaries, ignoring case.</summary>
    public static bool Contains(string text) => throw new NotImplementedException();
}
