using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using SdkGroundingResult = Geef.Sdk.Results.GroundingResult;
using DomainGroundingResult = Geef.Atelier.Core.Domain.Crew.Grounding.GroundingResult;

namespace Geef.Atelier.Tests.Pipeline;

/// <summary>
/// Tests that <see cref="MultiProviderGroundingStep"/> correctly decorates <see cref="IGroundingStep"/>,
/// calls all providers sequentially, and concatenates their enriched context blocks.
/// </summary>
public sealed class MultiProviderGroundingStepTests
{
    [Fact]
    public async Task RunAsync_WithNoProviders_ReturnsInnerResult()
    {
        var inner = new PassThroughGroundingStep("inner-context");
        var step  = new MultiProviderGroundingStep(
            inner, [], new StubGroundingProviderFactory([]), Guid.NewGuid(),
            NullLogger<MultiProviderGroundingStep>.Instance);

        var result = await step.RunAsync("briefing", CancellationToken.None);

        // No GroundingContext key should be set when there are no providers
        Assert.False(result.Context.TryGet(AtelierContextKeys.GroundingContext, out _));
    }

    [Fact]
    public async Task RunAsync_WithOneProvider_SetsGroundingContextKey()
    {
        var profile = MakeProfile("prov-a");
        var stubProvider = new StubGroundingProvider("prov-a", "Block from Provider A");
        var inner = new PassThroughGroundingStep("ignored");
        var step  = new MultiProviderGroundingStep(
            inner, [profile], new StubGroundingProviderFactory([stubProvider]), Guid.NewGuid(),
            NullLogger<MultiProviderGroundingStep>.Instance);

        var result = await step.RunAsync("briefing", CancellationToken.None);

        Assert.True(result.Context.TryGet(AtelierContextKeys.GroundingContext, out var ctx));
        Assert.Contains("Block from Provider A", ctx);
    }

    [Fact]
    public async Task RunAsync_WithTwoProviders_ConcatenatesBothBlocks()
    {
        var profiles = new[]
        {
            MakeProfile("prov-a"),
            MakeProfile("prov-b"),
        };
        var providers = new[]
        {
            new StubGroundingProvider("prov-a", "Block A"),
            new StubGroundingProvider("prov-b", "Block B"),
        };
        var inner = new PassThroughGroundingStep("inner");
        var step  = new MultiProviderGroundingStep(
            inner, profiles, new StubGroundingProviderFactory(providers), Guid.NewGuid(),
            NullLogger<MultiProviderGroundingStep>.Instance);

        var result = await step.RunAsync("briefing", CancellationToken.None);

        Assert.True(result.Context.TryGet(AtelierContextKeys.GroundingContext, out var ctx));
        Assert.Contains("Block A", ctx);
        Assert.Contains("Block B", ctx);
    }

    [Fact]
    public async Task RunAsync_CallsProvidersInOrder()
    {
        var callOrder = new List<string>();
        var profiles = new[] { MakeProfile("first"), MakeProfile("second") };
        var providers = new[]
        {
            new OrderTrackingProvider("first", callOrder),
            new OrderTrackingProvider("second", callOrder),
        };
        var inner = new PassThroughGroundingStep("inner");
        var step  = new MultiProviderGroundingStep(
            inner, profiles, new StubGroundingProviderFactory(providers), Guid.NewGuid(),
            NullLogger<MultiProviderGroundingStep>.Instance);

        await step.RunAsync("briefing", CancellationToken.None);

        Assert.Equal(["first", "second"], callOrder);
    }

    // --- helpers ---

    private static GroundingProviderProfile MakeProfile(string name) => new(
        Name: name,
        DisplayName: name,
        Description: "",
        ProviderType: name,
        ProviderSettings: new Dictionary<string, string>(),
        MaxQueriesPerRun: null,
        IsSystem: false);

    private sealed class PassThroughGroundingStep(string notes) : IGroundingStep
    {
        public Task<SdkGroundingResult> RunAsync(string input, CancellationToken ct)
        {
            var ctx = new RunContext().Set(AtelierContextKeys.GroundedBrief, input);
            return Task.FromResult(new SdkGroundingResult { Context = ctx, Notes = [notes] });
        }
    }

    private sealed class StubGroundingProvider(string providerType, string enrichedContext) : IGroundingProvider
    {
        public string ProviderType => providerType;

        public Task<DomainGroundingResult> EnrichAsync(string briefingText, GroundingProviderProfile profile, Guid runId, CancellationToken ct)
            => Task.FromResult(new DomainGroundingResult(profile.Name, enrichedContext, [], 0, null));
    }

    private sealed class OrderTrackingProvider(string providerType, List<string> order) : IGroundingProvider
    {
        public string ProviderType => providerType;

        public Task<DomainGroundingResult> EnrichAsync(string briefingText, GroundingProviderProfile profile, Guid runId, CancellationToken ct)
        {
            order.Add(providerType);
            return Task.FromResult(new DomainGroundingResult(profile.Name, "block", [], 0, null));
        }
    }

    private sealed class StubGroundingProviderFactory(IEnumerable<IGroundingProvider> providers) : IGroundingProviderFactory
    {
        private readonly Dictionary<string, IGroundingProvider> _map =
            providers.ToDictionary(p => p.ProviderType);

        public IGroundingProvider Create(string providerType)
            => _map.TryGetValue(providerType, out var p) ? p
               : throw new InvalidOperationException($"No provider for type '{providerType}'");

        public bool IsRegistered(string providerType) => _map.ContainsKey(providerType);
    }
}
