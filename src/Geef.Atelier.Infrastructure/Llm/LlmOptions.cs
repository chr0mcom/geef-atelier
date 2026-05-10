namespace Geef.Atelier.Infrastructure.Llm;

public sealed class LlmOptions
{
    public string Endpoint { get; set; } = "https://openrouter.ai/api/v1";
    public string ApiKey { get; set; } = string.Empty;
    public string DefaultModel { get; set; } = "anthropic/claude-opus-4.7";
    public int DefaultMaxTokens { get; set; } = 4096;

    /// <summary>
    /// Per-actor model configuration. Key is the actor name (matches <see cref="LlmActor"/> enum names).
    /// Falls back to <see cref="DefaultModel"/>/<see cref="DefaultMaxTokens"/> when actor is not present.
    /// </summary>
    public Dictionary<string, ActorConfig> Actors { get; set; } = new();

    public sealed class ActorConfig
    {
        public string Model { get; set; } = string.Empty;
        public int? MaxTokens { get; set; }
    }
}
