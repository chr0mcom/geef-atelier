namespace Geef.Atelier.Core.Domain;

/// <summary>
/// Records the LLM token usage and cost of a single finalizer-step actor.
/// Stored in a dedicated table (not <c>IterationActorCosts</c>) because finalizers run after
/// the last iteration and do not have a valid <c>IterationId</c>.
/// </summary>
public sealed class FinalizationActorCost
{
    public required Guid Id { get; init; }
    public required Guid RunId { get; init; }

    /// <summary>Name of the finalizer profile that incurred this cost.</summary>
    public required string ActorName { get; init; }

    public required string ModelName { get; init; }
    public required int InputTokens { get; init; }
    public required int OutputTokens { get; init; }
    public decimal? CostEur { get; init; }

    /// <summary>The provider that served this finalizer call, e.g. "claude-cli" or "openrouter". Null for pre-Step25 rows.</summary>
    public string? ProviderName { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
