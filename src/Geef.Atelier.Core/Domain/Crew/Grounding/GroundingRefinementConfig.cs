using Geef.Atelier.Core.Domain.Llm;

namespace Geef.Atelier.Core.Domain.Crew.Grounding;

/// <summary>
/// Configuration for an LLM-powered refinement pass applied to raw grounding results.
/// Produced from a <see cref="GroundingProviderProfile"/> when refinement keys are present.
/// </summary>
/// <param name="Binding">The LLM to invoke for refinement (provider, model, token budget, temperature).</param>
/// <param name="Mode">Whether to filter citations or synthesize a new text block.</param>
/// <param name="Instructions">Optional free-text instructions appended to the refinement system prompt.</param>
public sealed record GroundingRefinementConfig(
    LlmBinding Binding,
    GroundingRefinementMode Mode,
    string? Instructions);
