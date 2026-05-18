namespace Geef.Atelier.Web.Resources;

/// <summary>German-language field help texts displayed below each field in the Studio Edit-Step.</summary>
public static class StudioFieldHelps
{
    // --- Template fields ---
    public const string DisplayName =
        "Anzeigename des Templates in der Auswahlliste. Kurz und prägnant, z. B. \"Juristischer Vertragsprüfer\".";

    public const string Description =
        "Kurze Beschreibung, was dieses Crew-Template tut und für welche Aufgaben es geeignet ist.";

    public const string EvaluationStrategy =
        "Wie die Reviewer-Ergebnisse zusammengeführt werden. " +
        "Sequential: Reviewer nacheinander, jeder kann ein Veto einlegen. " +
        "Parallel: alle gleichzeitig, Mehrheit entscheidet. " +
        "FailFast: Ausführung stoppt beim ersten kritischen Fund. " +
        "Priority: Reviewer nach Priorität gewichtet.";

    // --- Profile fields (all types) ---
    public const string ProfileName =
        "Interner Bezeichner, eindeutig im System, nur Kleinbuchstaben und Bindestriche (kebab-case), z. B. \"quality-reviewer\".";

    public const string ProfileDisplayName =
        "Anzeigename des Profils in der Auswahlliste, kurz und prägnant.";

    public const string ProfileDescription =
        "Kurze Beschreibung, welche Rolle dieses Profil übernimmt und für welche Aufgaben es geeignet ist.";

    public const string Provider =
        "Der LLM-Anbieter, über den dieses Profil angebunden wird (z. B. OpenRouter, Anthropic, OpenAI).";

    public const string Model =
        "Das KI-Modell für dieses Profil. Wähle erst den Provider, dann das passende Modell.";

    public const string MaxTokens =
        "Maximale Antwortlänge in Tokens. Reviewer: 2 048 sind ein guter Standard. " +
        "Executor: 4 096 oder mehr für längere Texte.";

    public const string SystemPrompt =
        "Der System-Prompt, der das Verhalten dieses Profils steuert. " +
        "Je konkreter und fokussierter, desto besser die Ergebnisse. Maximal 8 000 Zeichen.";

    // --- Reviewer-spezifisch ---
    public const string ReviewerFocus =
        "Optionaler Fokus-Hinweis, auf was der Reviewer besonders achten soll (z. B. \"Stilsicherheit\" oder \"Faktengenauigkeit\").";

    // --- Advisor-spezifisch ---
    public const string AdvisorMode =
        "Die strategische Rolle des Advisors: " +
        "Strategic – Gesamtbild und Risiken. " +
        "Critical – Schwächen und Gegenargumente. " +
        "DevilsAdvocate – Argumentiert aktiv gegen den Plan.";

    public const string AdvisorTrigger =
        "Wann der Advisor eingesetzt wird: " +
        "BeforeFirstExecution – einmalig vor dem ersten Executor-Lauf. " +
        "BeforeEveryExecution – vor jedem Executor-Lauf. " +
        "OnConvergenceFailure – nur wenn die Konvergenz scheitert.";

    // --- Grounding-Provider-spezifisch ---
    public const string GroundingProviderType =
        "Art der Wissensquelle: Tavily für Web-Suche, VectorStore für eigene Dokumente.";

    public const string GroundingProviderSettings =
        "Typ-spezifische Einstellungen. Tavily: api_key erforderlich. VectorStore: collection_name erforderlich.";

    public const string MaxQueriesPerRun =
        "Wie viele Suchanfragen pro Ausführung maximal gestellt werden dürfen (1–5).";
}
