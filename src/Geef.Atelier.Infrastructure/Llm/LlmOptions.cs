namespace Geef.Atelier.Infrastructure.Llm;

public sealed class LlmOptions
{
    /// <summary>Named provider configurations — keyed by provider name (e.g. "openrouter", "cli").</summary>
    public Dictionary<string, ProviderConfig> Providers { get; set; } = new();

    /// <summary>Per-actor configuration. Key matches actor name used by pipeline steps and reviewers.</summary>
    public Dictionary<string, ActorConfig> Actors { get; set; } = new();

    /// <summary>Fallback provider name when an actor's Provider field is empty.</summary>
    public string DefaultProvider { get; set; } = "openrouter";

    public int DefaultMaxTokens { get; set; } = 4096;

    public sealed class ProviderConfig
    {
        public string Endpoint { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
    }

    public sealed class ActorConfig
    {
        /// <summary>Key into <see cref="Providers"/>. Falls back to <see cref="DefaultProvider"/> when empty.</summary>
        public string Provider { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public int? MaxTokens { get; set; }
    }
}
