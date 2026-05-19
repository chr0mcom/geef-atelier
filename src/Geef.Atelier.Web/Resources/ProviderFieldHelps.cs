namespace Geef.Atelier.Web.Resources;

/// <summary>German-language field help texts displayed below each field in the Provider editor forms.</summary>
public static class ProviderFieldHelps
{
    public const string Name =
        "Eindeutiger Bezeichner für diesen Provider. Wird intern und in Profile-Formularen als Referenz verwendet. Nur Kleinbuchstaben, Zahlen und Bindestriche. Wird automatisch mit 'custom-' präfixiert.";

    public const string DisplayName =
        "Anzeigename in der UI, z.B. im Provider-Dropdown bei Profile-Erstellung.";

    public const string Description =
        "Beschreibung wofür dieser Provider geeignet ist — wird in der Provider-Liste angezeigt.";

    public const string IsActive =
        "Inaktive Provider werden in neuen Profil-Formularen nicht angezeigt. Bestehende Profile, die diesen Provider referenzieren, funktionieren weiterhin.";

    // HTTP-spezifisch
    public const string HttpEndpoint =
        "Basis-URL des OpenAI-kompatiblen Endpoints, z.B. 'https://api.openai.com/v1'. Kein Trailing-Slash.";

    public const string HttpApiKeyEnv =
        "Name der Umgebungsvariable die den API-Key enthält, z.B. 'OPENAI_API_KEY'. Der Wert wird zur Laufzeit aus dem Server-Environment gelesen — niemals in der Datenbank gespeichert.";

    public const string HttpEndpointEnvOverride =
        "Optionale Umgebungsvariable die die Endpoint-URL überschreibt. Nützlich für lokale Instanzen wie Ollama.";

    public const string HttpAuthHeaderName =
        "Name des Auth-Headers. Standard: 'Authorization'. Für Azure OpenAI: 'api-key'.";

    public const string HttpAuthHeaderFormat =
        "Format des Auth-Header-Werts mit '{key}' als Platzhalter. Standard: 'Bearer {key}'. Für api-key-Auth: '{key}'.";

    public const string HttpModelsEndpoint =
        "Pfad zum Models-Discovery-Endpoint relativ zur Basis-URL, z.B. '/models'. Leer lassen wenn der Provider keinen Models-Endpoint hat — dann manuelle Modell-Liste verwenden.";

    public const string HttpDefaultHeaders =
        "Optionale zusätzliche HTTP-Header die bei jedem Request mitgesendet werden, z.B. HTTP-Referer oder API-Version-Header. Format: ein Header pro Zeile als 'Key: Value'.";

    public const string HttpManualModelList =
        "Manuelle Modell-Liste, wenn kein Models-Endpoint verfügbar ist. Ein Modell pro Zeile.";

    public const string HttpCostPerInputToken =
        "Optionaler Preis pro Input-Token in EUR für Cost-Tracking. Leer lassen wenn unbekannt oder nicht relevant.";

    public const string HttpCostPerOutputToken =
        "Optionaler Preis pro Output-Token in EUR für Cost-Tracking. Leer lassen wenn unbekannt oder nicht relevant.";

    // CLI-spezifisch
    public const string CliKind =
        "CLI-Typ. Bei Built-in-CLIs (Claude, Codex, Gemini) sind die meisten Felder vorausgefüllt und schreibgeschützt. Bei 'Generic' sind alle Felder editierbar.";

    public const string CliMaxConcurrent =
        "Maximale Anzahl gleichzeitiger CLI-Aufrufe für diesen Provider. Verhindert Rate-Limiting bei Subscription-basierten CLIs.";

    public const string CliBinary =
        "Pfad oder Name des CLI-Binaries, z.B. 'gemini' oder '/usr/local/bin/mycli'.";

    public const string CliPromptArgsTemplate =
        "Argument-Template für den CLI-Aufruf. '{prompt}' und '{model}' werden ersetzt. Beispiel: -p {prompt} --model {model} (ein Argument pro Zeile).";

    public const string CliStdinMode =
        "Wenn aktiviert, wird der Prompt per stdin übergeben statt als Argument.";

    public const string CliOutputFormat =
        "Format der CLI-Ausgabe: text (Freitext), openai-json (OpenAI-kompatibles JSON), jsonl (Streaming-JSON, letztes Event).";

    public const string CliOutputJsonPath =
        "JSON-Pfad zum Antwort-Feld in der Ausgabe, z.B. 'response'. Nur für json/jsonl relevant.";

    public const string CliModels =
        "Verfügbare Modelle für diesen CLI-Provider. Eines pro Zeile, z.B. 'google/gemini-2.5-pro'.";
}
