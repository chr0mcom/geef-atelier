using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Core.Domain.Llm;

namespace Geef.Atelier.Core.Domain.Crew.Finalizers;

/// <summary>
/// Typed wrapper over the <c>Settings</c> dictionary for <see cref="FinalizerType.Transform"/> profiles.
/// The transform finalizer sends the final text through an LLM pass using the configured system prompt.
/// </summary>
/// <param name="SystemPrompt">Transform instruction.</param>
/// <param name="Provider">Provider of the transform LLM.</param>
/// <param name="Model">Model of the transform LLM.</param>
/// <param name="MaxTokens">Output token cap.</param>
/// <param name="Temperature">Optional sampling temperature.</param>
/// <param name="ModelSlot">
/// Model-matrix slot (<see cref="ModelSlot.TransformRewrite"/> or <see cref="ModelSlot.TransformCondense"/>) when the
/// binding is managed; <c>null</c> for a manual binding.
/// </param>
public sealed record TransformSettings(
    string SystemPrompt,
    string Provider,
    string Model,
    int MaxTokens,
    double? Temperature = null,
    ModelSlot? ModelSlot = null)
{
    public const string KeySystemPrompt = "SystemPrompt";
    public const string KeyProvider = "Provider";
    public const string KeyModel = "Model";
    public const string KeyMaxTokens = "MaxTokens";
    public const string KeyTemperature = "Temperature";

    /// <summary>Settings key marking the binding as managed by the model matrix (value: a <see cref="Models.ModelSlot"/> name).</summary>
    public const string KeyModelSlot = "ModelSlot";

    /// <summary>
    /// Reads the settings. A missing provider or model falls back to the matrix primary of the stored slot,
    /// or of <see cref="ModelSlot.TransformCondense"/> when no slot is stored.
    /// </summary>
    public static TransformSettings From(Dictionary<string, string> settings)
    {
        var slot = ModelSlotParser.ParseOrNull(settings.GetValueOrDefault(KeyModelSlot));
        var fallback = ModelMatrix.Primary(slot ?? Models.ModelSlot.TransformCondense);
        var provider = settings.GetValueOrDefault(KeyProvider);
        var model = settings.GetValueOrDefault(KeyModel);
        var hasBinding = !string.IsNullOrWhiteSpace(provider) && !string.IsNullOrWhiteSpace(model);

        return new TransformSettings(
            SystemPrompt: settings.GetValueOrDefault(KeySystemPrompt, string.Empty),
            Provider: hasBinding ? provider! : fallback.Provider,
            Model: hasBinding ? model! : fallback.Model,
            MaxTokens: int.TryParse(settings.GetValueOrDefault(KeyMaxTokens), out var m) ? m : 60000,
            Temperature: double.TryParse(settings.GetValueOrDefault(KeyTemperature), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : null,
            ModelSlot: slot);
    }

    public Dictionary<string, string> ToDict()
    {
        var dict = new Dictionary<string, string>
        {
            [KeySystemPrompt] = SystemPrompt,
            [KeyProvider] = Provider,
            [KeyModel] = Model,
            [KeyMaxTokens] = MaxTokens.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        if (Temperature.HasValue)
            dict[KeyTemperature] = Temperature.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (ModelSlot.HasValue)
            dict[KeyModelSlot] = ModelSlot.Value.ToString();
        return dict;
    }

    public LlmBinding Binding => new(Provider, Model, MaxTokens, Temperature);

    public TransformSettings WithBinding(LlmBinding b) =>
        this with { Provider = b.Provider, Model = b.Model, MaxTokens = b.MaxTokens, Temperature = b.Temperature };
}
