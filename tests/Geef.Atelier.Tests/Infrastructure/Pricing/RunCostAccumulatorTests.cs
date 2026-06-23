using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Infrastructure.Pricing;

namespace Geef.Atelier.Tests.Infrastructure.Pricing;

public sealed class RunCostAccumulatorTests
{
    [Fact]
    public void RecordActorCost_SingleEntry_AppearsInFlush()
    {
        var acc = new RunCostAccumulator();

        acc.RecordActorCost(1, ActorType.Executor, "exec", "model", 100, 50, 0.01m);

        var flushed = acc.Flush();
        Assert.Single(flushed);
        var item = flushed[0];
        Assert.Equal(1, item.IterationNumber);
        Assert.Equal(ActorType.Executor, item.ActorType);
        Assert.Equal("exec", item.ActorName);
        Assert.Equal("model", item.ModelName);
        Assert.Equal(100, item.InputTokens);
        Assert.Equal(50, item.OutputTokens);
        Assert.Equal(0.01m, item.CostEur);
        // Token breakdown defaults to 0 when not supplied.
        Assert.Equal(0, item.CachedInputTokens);
        Assert.Equal(0, item.ReasoningTokens);
    }

    [Fact]
    public void RecordActorCost_TokenBreakdown_IsPreserved()
    {
        var acc = new RunCostAccumulator();

        acc.RecordActorCost(2, ActorType.Reviewer, "rev", "model", 1000, 200, 0.02m,
            providerName: "codex-cli", cachedInputTokens: 800, reasoningTokens: 64);

        var item = acc.Flush()[0];
        Assert.Equal("codex-cli", item.ProviderName);
        Assert.Equal(800, item.CachedInputTokens);
        Assert.Equal(64, item.ReasoningTokens);
    }

    [Fact]
    public void Flush_ClearsAccumulator()
    {
        var acc = new RunCostAccumulator();
        acc.RecordActorCost(1, ActorType.Executor, "exec", "model", 10, 5, null);

        acc.Flush();
        var second = acc.Flush();

        Assert.Empty(second);
    }

    [Fact]
    public void RecordActorCost_MultipleEntries_AllReturnedByFlush()
    {
        var acc = new RunCostAccumulator();
        acc.RecordActorCost(1, ActorType.Executor, "exec", "model", 10, 5, 0.001m);
        acc.RecordActorCost(1, ActorType.Reviewer, "rev1", "model", 8, 4, 0.0005m);
        acc.RecordActorCost(1, ActorType.Reviewer, "rev2", "model", 8, 4, 0.0005m);

        var flushed = acc.Flush();

        Assert.Equal(3, flushed.Count);
    }

    [Fact]
    public void RecordActorCost_NullCost_IsPreserved()
    {
        var acc = new RunCostAccumulator();

        acc.RecordActorCost(1, ActorType.Executor, "exec", "unknown-model", 10, 5, null);

        var flushed = acc.Flush();
        Assert.Single(flushed);
        Assert.Null(flushed[0].CostEur);
    }

    [Fact]
    public void RecordActorCost_ConcurrentWrites_AllEntriesPresent()
    {
        var acc = new RunCostAccumulator();
        const int threadCount = 10;
        const int recordsPerThread = 100;

        var threads = Enumerable.Range(0, threadCount)
            .Select(_ => new Thread(() =>
            {
                for (var i = 0; i < recordsPerThread; i++)
                    acc.RecordActorCost(1, ActorType.Reviewer, "rev", "model", 1, 1, 0.001m);
            }))
            .ToList();

        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        var flushed = acc.Flush();
        Assert.Equal(threadCount * recordsPerThread, flushed.Count);
    }
}
