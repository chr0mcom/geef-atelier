using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Tests.Llm;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Pipeline;

/// <summary>
/// F7 (Etappe 0, review round 3): a crew with sources but without a grounding provider factory fails loudly when the
/// pipeline is built, instead of running without its sources; a crew without sources still builds without one.
/// </summary>
public sealed class AtelierPipelineFactoryRequiresGroundingFactoryTests
{
    private static readonly GroundingProviderProfile Source = new(
        "web", "Web", "d", "tavily", new Dictionary<string, string>(), MaxQueriesPerRun: null, IsSystem: false);

    [Fact]
    public void BuildWithSourcesButWithoutFactoryThrows()
    {
        var snapshot = TestCrewSnapshots.Klassik with { GroundingProviders = [Source] };

        var ex = Assert.Throws<InvalidOperationException>(() => AtelierPipelineFactory.Build(
            snapshot, new TestLlmClientResolver(new FakeLlmClient()), Options.Create(new ConvergenceOptions()), new InMemoryRunWarnings()));

        Assert.Equal(AtelierPipelineFactory.MissingGroundingFactoryMessage, ex.Message);
    }

    [Fact]
    public void BuildWithSeedDraftWithSourcesButWithoutFactoryThrows()
    {
        var snapshot = TestCrewSnapshots.Klassik with { GroundingProviders = [Source] };

        Assert.Throws<InvalidOperationException>(() => AtelierPipelineFactory.BuildWithSeedDraft(
            snapshot, new TestLlmClientResolver(new FakeLlmClient()), Options.Create(new ConvergenceOptions()), "seed", new InMemoryRunWarnings()));
    }

    [Fact]
    public void BuildWithoutSourcesNeedsNoFactory()
    {
        var runner = AtelierPipelineFactory.Build(
            TestCrewSnapshots.Klassik, new TestLlmClientResolver(new FakeLlmClient()), Options.Create(new ConvergenceOptions()), new InMemoryRunWarnings());

        Assert.NotNull(runner);
    }
}
