namespace Geef.Atelier.Core.Domain;

/// <summary>Per-actor-call cost record for a single iteration.</summary>
public sealed class IterationActorCostEntity
{
    public required Guid Id { get; init; }
    public required Guid IterationId { get; init; }
    public required ActorType ActorType { get; init; }

    /// <summary>Profile name, e.g. "default-executor" or "briefing-fidelity".</summary>
    public required string ActorName { get; init; }

    /// <summary>Resolved model identifier, e.g. "anthropic/claude-opus-4.7".</summary>
    public required string ModelName { get; init; }

    public required int InputTokens { get; init; }
    public required int OutputTokens { get; init; }

    /// <summary>Cost in EUR, or null when the model is not in the pricing table.</summary>
    public decimal? CostEur { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public IterationEntity Iteration { get; init; } = null!;
}
