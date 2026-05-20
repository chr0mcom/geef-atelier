using Geef.Atelier.Core.Domain.Llm;

namespace Geef.Atelier.Core.Domain.Crew.Finalizers;

/// <summary>
/// Typed wrapper over the <c>Settings</c> dictionary for <see cref="FinalizerType.Transform"/> profiles.
/// The transform finalizer sends the final text through an LLM pass using the configured system prompt.
/// </summary>
public sealed record TransformSettings(
    string SystemPrompt,
    string Provider,
    string Model,
    int MaxTokens,
    double? Temperature = null)
{
    public const string KeySystemPrompt = "SystemPrompt";
    public const string KeyProvider = "Provider";
    public const string KeyModel = "Model";
    public const string KeyMaxTokens = "MaxTokens";
    public const string KeyTemperature = "Temperature";

    public static TransformSettings From(Dictionary<string, string> settings) => new(
        SystemPrompt: settings.GetValueOrDefault(KeySystemPrompt, string.Empty),
        Provider: settings.GetValueOrDefault(KeyProvider, "codex-cli"),
        Model: settings.GetValueOrDefault(KeyModel, "gpt-5.5"),
        MaxTokens: int.TryParse(settings.GetValueOrDefault(KeyMaxTokens), out var m) ? m : 4096,
        Temperature: double.TryParse(settings.GetValueOrDefault(KeyTemperature), out var t) ? t : null);

    public Dictionary<string, string> ToDict()
    {
        var dict = new Dictionary<string, string>
        {
            [KeySystemPrompt] = SystemPrompt,
            [KeyProvider] = Provider,
            [KeyModel] = Model,
            [KeyMaxTokens] = MaxTokens.ToString(),
        };
        if (Temperature.HasValue)
            dict[KeyTemperature] = Temperature.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return dict;
    }

    public LlmBinding Binding => new(Provider, Model, MaxTokens, Temperature);

    public TransformSettings WithBinding(LlmBinding b) =>
        this with { Provider = b.Provider, Model = b.Model, MaxTokens = b.MaxTokens, Temperature = b.Temperature };
}
