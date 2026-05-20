namespace Geef.Atelier.Core.Domain.Crew;

/// <summary>
/// Records the LLM token usage and cost of a single grounding-refinement actor.
/// Stored in a dedicated table because grounding runs before the first iteration and
/// does not have a valid <c>IterationId</c>.
/// </summary>
public sealed class GroundingActorCost
{
    public required Guid Id { get; init; }
    public required Guid RunId { get; init; }

    /// <summary>Name of the grounding-provider profile that triggered this refinement cost.</summary>
    public required string GroundingProviderName { get; init; }

    /// <summary>Name of the actor that incurred this cost (e.g. <c>"GroundingRefiner"</c>).</summary>
    public required string ActorName { get; init; }

    /// <summary>The LLM provider that served this call, e.g. <c>"openrouter"</c>. Null when unknown.</summary>
    public string? ProviderName { get; init; }

    /// <summary>The model identifier used for the refinement call. Null when unknown.</summary>
    public string? ModelName { get; init; }

    public required int InputTokens { get; init; }
    public required int OutputTokens { get; init; }
    public decimal? CostEur { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
