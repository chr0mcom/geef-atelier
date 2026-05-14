namespace Geef.Atelier.Application.Crew.TemplateStudio;

/// <summary>Configuration options for the Template Studio meta-LLM calls.</summary>
public sealed class TemplateStudioOptions
{
    /// <summary>OpenRouter model identifier for the meta-LLM analysis call.</summary>
    public string Model { get; set; } = "anthropic/claude-sonnet-4-5";

    /// <summary>Max tokens for the meta-LLM analysis response.</summary>
    public int MaxTokens { get; set; } = 4096;

    /// <summary>Cosine similarity threshold above which an existing profile is considered a duplicate.</summary>
    public double SimilarityThreshold { get; set; } = 0.85;
}
