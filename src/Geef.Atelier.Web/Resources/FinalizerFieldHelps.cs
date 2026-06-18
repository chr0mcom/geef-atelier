namespace Geef.Atelier.Web.Resources;

/// <summary>Field help texts displayed below each field in the Finalizer Editor.</summary>
public static class FinalizerFieldHelps
{
    public const string TransformProvider =
        "The LLM provider for the text transformation. Only active providers are available for selection.";

    public const string TransformModel =
        "The model for the text transformation. For tone changes and style adjustments, cheap models are usually sufficient.";

    public const string TransformMaxTokens =
        "Maximum number of tokens for the transformation output. At least 1024.";

    public const string TransformTemperature =
        "Creativity level of the AI: empty = provider default, 0.0 = deterministic, 2.0 = very creative. " +
        "Recommended for text transformations: 0.3–0.7.";
}
