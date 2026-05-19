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

    // --- Finalizer-spezifisch ---
    public const string FinalizerType =
        "Art des Finalizers: " +
        "FileExport – exportiert den Text in eine Datei (Markdown, HTML, PDF, DOCX, TXT). " +
        "MetadataEnrich – reichert den Text mit Metadaten an (Front-Matter, Wortzahl, Lesbarkeit). " +
        "ExternalSink – sendet den Text an einen Webhook oder per E-Mail. " +
        "Transform – verändert den Text durch ein KI-Modell (z. B. Anti-AI-Stimme).";

    public const string FinalizerFileFormat =
        "Zieldateiformat: markdown, html, pdf, docx, txt. PDF und DOCX werden serverseitig generiert.";

    public const string FinalizerEnricherType =
        "Art der Metadaten-Anreicherung: " +
        "front-matter – YAML-Header mit Titel, Erstellungszeit und Wortzahl. " +
        "word-count-footer – Wortzahl und Lesezeit als Fußzeile. " +
        "reading-level – Flesch-Kincaid-Leseniveau als Hinweis im Text.";

    public const string FinalizerSinkType =
        "Zielkanal: webhook – HTTP-POST an eine URL. email – E-Mail-Versand via SMTP.";

    public const string FinalizerWebhookUrl =
        "Ziel-URL für den Webhook-POST. Die URL wird nicht in Logs gespeichert.";

    public const string FinalizerWebhookAuthHeader =
        "Optionaler HTTP-Autorisierungs-Header (z. B. \"Bearer my-token\"). " +
        "Wird nicht angezeigt und nicht geloggt.";

    public const string FinalizerEmailTo =
        "Empfänger-E-Mail-Adresse. SMTP muss serverseitig konfiguriert sein (Umgebungsvariablen).";

    public const string FinalizerEmailSubject =
        "Betreffzeile der E-Mail. Unterstützt Platzhalter: {run-id}, {template}, {timestamp}.";

    public const string FinalizerEmailAttach =
        "Wenn aktiviert, wird der Text als Dateianhang mitgeschickt (Dateiname aus Profil-Name).";

    public const string FinalizerTransformSystemPrompt =
        "Anweisung für die KI-Transformation. Sei konkret: was soll geändert werden und was nicht. " +
        "Beispiel: \"Schreibe den Text in einer natürlichen, menschlichen Stimme um. Vermeide KI-typische Satzkonstruktionen.\" " +
        "Endet immer mit: 'Respond in the language of the input text.'";

    public const string FinalizerProfiles =
        "Finalizer werden nach dem Konvergenz-Schritt in der angegebenen Reihenfolge ausgeführt. " +
        "Transform-Finalizer ändern den Text; Export- und Sink-Finalizer erzeugen Artefakte. " +
        "Reihenfolge: erst transformieren, dann exportieren.";

    public const string RunFinalizersOnMaxAttempts =
        "Wenn aktiviert, laufen die Finalizer auch wenn die Konvergenz nach maximalen Iterationen scheitert — " +
        "der letzte Executor-Entwurf wird dann als Ergebnis verwendet.";
}
