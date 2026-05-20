namespace Geef.Atelier.Web.Resources;

/// <summary>German-language field help texts displayed below each field in the Grounding Provider Editor.</summary>
public static class GroundingFieldHelps
{
    public const string RefinementEnabled = "KI-Refinement aktivieren: Der Refiner filtert Rohergebnisse, bevor sie ans Briefing angehängt werden.";
    public const string RefinementProvider = "Der LLM-Anbieter für das Refinement. Günstige Modelle sind oft ausreichend (z. B. Gemini Flash).";
    public const string RefinementModel = "Das Modell für das Refinement. Für Filteraufgaben reichen schnelle, kostengünstige Modelle.";
    public const string RefinementMaxTokens = "Maximale Token-Anzahl für die Refinement-Antwort. Mindestens 256.";
    public const string RefinementTemperature = "Kreativitätsstufe: leer = Anbieter-Standard, 0.0 = deterministisch, 2.0 = sehr kreativ. Für Filter empfohlen: 0.0.";
    public const string RefinementMode = "Filter: Quellen werden behalten oder verworfen. Synthesize: alle Quellen werden zu einem kohärenten Text zusammengefasst.";
    public const string RefinementInstructions = "Optionale Zusatz-Anweisungen für den Refiner (z. B. 'Verwirf alle Quellen ohne Datum 2025/2026').";

    // Static-context
    public const string StaticContextLabel =
        "Quellenname für die Attribution (z. B. „Markenstimme“, „Glossar Q2 2026“). Erscheint in der Grounding-Visualisierung.";

    public const string StaticContextContent =
        "Der kuratierte Text, der bei jedem Run unverändert in den Kontext eingefügt wird. " +
        "Soft-Limit 50.000 Zeichen — sehr große Texte (>50k) gehören besser in die Knowledge-Base " +
        "(nutze stattdessen einen Vector-Store-Provider).";

    // URL-fetch
    public const string UrlFetchUrls =
        "Eine URL pro Zeile, nur https:// oder http://. " +
        "Wichtig: Nur öffentlich erreichbare URLs — interne Adressen " +
        "(localhost, 127.0.0.1, 10.x, 192.168.x, Cloud-Metadata) werden aus Sicherheitsgründen blockiert. " +
        "JavaScript-intensive Seiten liefern möglicherweise wenig Inhalt (kein Browser-Rendering).";

    public const string UrlFetchMaxContent =
        "Maximale Zeichenanzahl pro URL nach HTML-Bereinigung. Default 8.000. " +
        "Höhere Werte erhöhen den Kontext-Verbrauch.";

    public const string UrlFetchStripBoilerplate =
        "Entfernt Navigation, Werbung, Cookie-Banner und ähnliche Boilerplate-Elemente beim Parsen. " +
        "Empfohlen: aktiviert. Der KI-Refiner kann zusätzlich Restmüll filtern.";

    // News-search
    public const string NewsSearchRecencyDays =
        "Nur News aus den letzten N Tagen. Default 7, Maximum 365.";

    public const string NewsSearchMaxResults =
        "Maximale Anzahl News-Treffer pro Anfrage. Default 5, Maximum 20.";

    public const string NewsSearchDepth =
        "Tavily-Suchtiefe: basic (schneller, günstiger) oder advanced (gründlicher, höhere Credit-Kosten).";
}
