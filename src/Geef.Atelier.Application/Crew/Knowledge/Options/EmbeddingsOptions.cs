namespace Geef.Atelier.Application.Crew.Knowledge.Options;

/// <summary>
/// Configuration for the embedding provider used by the knowledge-base indexing pipeline.
/// Bound from <c>Embeddings</c> in application settings.
/// </summary>
public sealed class EmbeddingsOptions
{
    /// <summary>Provider identifier (default: <c>openrouter</c>).</summary>
    public string Provider { get; set; } = "openrouter";

    /// <summary>Fully-qualified model identifier (default: <c>openai/text-embedding-3-small</c>).</summary>
    public string Model { get; set; } = "openai/text-embedding-3-small";

    /// <summary>Number of dimensions produced by <see cref="Model"/> (default: 1536).</summary>
    public int Dimensions { get; set; } = 1536;

    /// <summary>Base URL of the embedding API endpoint (default: <c>https://openrouter.ai/api/v1</c>).</summary>
    public string Endpoint { get; set; } = "https://openrouter.ai/api/v1";

    /// <summary>Cost in USD per one million tokens (default: 0.02).</summary>
    public double CostPerMillionTokensUsd { get; set; } = 0.02;

    /// <summary>Exchange rate used to convert USD costs to EUR (default: 0.92).</summary>
    public double UsdToEurRate { get; set; } = 0.92;

    /// <summary>Maximum number of texts to send in a single batch request (default: 100).</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Whether the provider is allowed to fall back to alternative models on failure (default: <c>true</c>).</summary>
    public bool AllowFallbacks { get; set; } = true;

    /// <summary>HTTP request timeout in seconds (default: 30).</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;
}
