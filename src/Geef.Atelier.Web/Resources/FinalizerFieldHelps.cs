namespace Geef.Atelier.Web.Resources;

/// <summary>German-language field help texts displayed below each field in the Finalizer Editor.</summary>
public static class FinalizerFieldHelps
{
    public const string TransformProvider =
        "Der LLM-Anbieter für die Text-Transformation. Nur aktive Anbieter stehen zur Auswahl.";

    public const string TransformModel =
        "Das Modell für die Text-Transformation. Für Tone-Changes und Stil-Anpassungen reichen meist günstige Modelle.";

    public const string TransformMaxTokens =
        "Maximale Anzahl Token für die Ausgabe der Transformation. Mindestens 1024.";

    public const string TransformTemperature =
        "Kreativitätsstufe der KI: leer = Anbieter-Standard, 0.0 = deterministisch, 2.0 = sehr kreativ. " +
        "Für Text-Transformationen empfohlen: 0.3–0.7.";
}
