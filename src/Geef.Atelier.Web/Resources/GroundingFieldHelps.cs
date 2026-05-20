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
}
