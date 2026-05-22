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

    // Academic-search
    public const string AcademicSource =
        "Wissenschafts-API: arXiv (Preprints CS/Physik/Math, kein Key), " +
        "Semantic Scholar (breite Abdeckung, optionaler Key für höhere Rate-Limits), " +
        "OpenAlex (sehr breit, modern, kostenlos).";

    public const string AcademicMaxPapers =
        "Maximale Anzahl Paper pro Anfrage. Default 5, empfohlen 3–10.";

    public const string AcademicDateFrom =
        "Nur Paper ab diesem Datum (ISO-Format: YYYY oder YYYY-MM-DD). Leer = kein Datumsfilter.";

    public const string AcademicFields =
        "Optionale Suchfeld-Einschränkung (z. B. 'ti' für Titel bei arXiv). Leer = alle Felder.";

    public const string AcademicApiKeyEnv =
        "ENV-Variablen-Name für den Semantic-Scholar-API-Key (z. B. SEMANTIC_SCHOLAR_API_KEY). " +
        "Wichtig: Hier den Variablennamen eintragen, nicht den Key selbst! Leer = kein Key (niedrigere Rate-Limits).";

    // REST-API
    public const string RestApiUrl =
        "Vollständige URL des Endpunkts (https://…). Platzhalter {briefing} wird durch das URL-kodierte Briefing ersetzt. " +
        "Sicherheitshinweis: Interne Adressen (localhost, 10.x, 192.168.x, 172.16–31.x, 169.254.x) werden aus " +
        "Sicherheitsgründen blockiert (SSRF-Schutz). Nur JSON-Antworten werden unterstützt.";

    public const string RestApiMethod =
        "HTTP-Methode: GET (Standard, Parameter via URL) oder POST (Daten im Body).";

    public const string RestApiHeaders =
        "Zusätzliche HTTP-Header als JSON-Objekt, z. B. {\"Accept\": \"application/json\"}. " +
        "Keine Secrets hier hinterlegen — dafür Authentifizierungs-ENV-Variable verwenden.";

    public const string RestApiBodyTemplate =
        "Request-Body-Template für POST-Anfragen. {briefing} wird durch das JSON-escaped Briefing ersetzt. " +
        "Beispiel: {\"query\": \"{briefing}\", \"limit\": 10}";

    public const string RestApiResponsePath =
        "JSONPath zum relevanten Teil der Antwort, z. B. $.results oder $.data[*].content. " +
        "Leer = gesamte Antwort verwenden.";

    public const string RestApiMaxItems =
        "Maximale Anzahl extrahierter Array-Items. Default 10.";

    public const string RestApiAuthHeaderEnv =
        "ENV-Variablen-Name für den Auth-Token (z. B. MY_API_TOKEN). " +
        "Wichtig: Hier den Variablennamen eintragen, nicht den Token selbst! " +
        "Der Token wird zur Laufzeit aus der Umgebungsvariable gelesen und taucht niemals in der Datenbank auf.";

    public const string RestApiAuthHeaderName =
        "Name des Auth-Headers. Standard: Authorization. Andere Beispiele: X-Api-Key, Token.";

    public const string RestApiAuthHeaderFormat =
        "Format des Header-Werts; {token} wird durch den aufgelösten Token ersetzt. Standard: Bearer {token}.";
}
