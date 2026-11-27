namespace Geef.Atelier.Infrastructure.Llm;

public sealed class LlmOptions
{
    /// <summary>
    /// Fallback provider configurations used when a provider name is not found via the provider service.
    /// Keyed by provider name (e.g. <c>"openrouter"</c>, <c>"claude-cli"</c>).
    /// Populated from the <c>Llm:ProvidersFallback</c> config section; <c>Llm:Providers</c> is also accepted
    /// for backward compatibility.
    /// </summary>
    public Dictionary<string, ProviderConfig> ProvidersFallback { get; set; } = new();

    /// <summary>Backward-compatible alias; populated from <c>Llm:Providers</c> in appsettings.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Dictionary<string, ProviderConfig> Providers
    {
        get => ProvidersFallback;
        set => ProvidersFallback = value;
    }

    public int DefaultMaxTokens { get; set; } = 16384;

    public sealed class ProviderConfig
    {
        public string Endpoint { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
    }
}
