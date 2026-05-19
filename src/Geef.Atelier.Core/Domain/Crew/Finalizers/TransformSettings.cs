namespace Geef.Atelier.Core.Domain.Crew.Finalizers;

/// <summary>
/// Typed wrapper over the <c>Settings</c> dictionary for <see cref="FinalizerType.Transform"/> profiles.
/// The transform finalizer sends the final text through an LLM pass using the configured system prompt.
/// </summary>
public sealed record TransformSettings(
    string SystemPrompt,
    string Provider,
    string Model,
    int MaxTokens)
{
    public const string KeySystemPrompt = "SystemPrompt";
    public const string KeyProvider = "Provider";
    public const string KeyModel = "Model";
    public const string KeyMaxTokens = "MaxTokens";

    public static TransformSettings From(Dictionary<string, string> settings) => new(
        SystemPrompt: settings.GetValueOrDefault(KeySystemPrompt, string.Empty),
        Provider: settings.GetValueOrDefault(KeyProvider, "codex-cli"),
        Model: settings.GetValueOrDefault(KeyModel, "gpt-5.5"),
        MaxTokens: int.TryParse(settings.GetValueOrDefault(KeyMaxTokens), out var m) ? m : 4096);

    public Dictionary<string, string> ToDict() => new()
    {
        [KeySystemPrompt] = SystemPrompt,
        [KeyProvider] = Provider,
        [KeyModel] = Model,
        [KeyMaxTokens] = MaxTokens.ToString(),
    };
}
