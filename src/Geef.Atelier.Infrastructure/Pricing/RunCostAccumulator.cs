using System.Collections.Concurrent;
using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Core.Domain;

namespace Geef.Atelier.Infrastructure.Pricing;

internal sealed class RunCostAccumulator : ICostAccumulator
{
    private readonly ConcurrentBag<PendingActorCost> _pending = new();

    public void RecordActorCost(
        int iterationNumber,
        ActorType actorType,
        string actorName,
        string modelName,
        int inputTokens,
        int outputTokens,
        decimal? costEur,
        string? providerName = null,
        int cachedInputTokens = 0,
        int reasoningTokens = 0)
        => _pending.Add(new PendingActorCost(
            iterationNumber, actorType, actorName, modelName, inputTokens, outputTokens, costEur,
            providerName, cachedInputTokens, reasoningTokens));

    public IReadOnlyList<PendingActorCost> Flush()
    {
        var items = _pending.ToArray();
        while (!_pending.IsEmpty) _pending.TryTake(out _);
        return items;
    }
}
