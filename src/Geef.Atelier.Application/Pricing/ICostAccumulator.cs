using Geef.Atelier.Core.Domain;

namespace Geef.Atelier.Application.Pricing;

/// <summary>
/// Thread-safe in-memory accumulator for per-actor LLM costs within a single pipeline run.
/// Records are flushed and persisted by the orchestrator after the pipeline completes.
/// </summary>
public interface ICostAccumulator
{
    /// <summary>Records the token usage and cost for one actor call.</summary>
    void RecordActorCost(
        int iterationNumber,
        ActorType actorType,
        string actorName,
        string modelName,
        int inputTokens,
        int outputTokens,
        decimal? costEur,
        string? providerName = null,
        int cachedInputTokens = 0,
        int reasoningTokens = 0);

    /// <summary>Returns all accumulated records and clears the internal buffer.</summary>
    IReadOnlyList<PendingActorCost> Flush();
}

public sealed record PendingActorCost(
    int IterationNumber,
    ActorType ActorType,
    string ActorName,
    string ModelName,
    int InputTokens,
    int OutputTokens,
    decimal? CostEur,
    string? ProviderName = null,
    int CachedInputTokens = 0,
    int ReasoningTokens = 0);
