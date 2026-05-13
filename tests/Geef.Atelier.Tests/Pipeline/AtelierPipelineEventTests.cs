using Geef.Atelier.Core.Domain;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Tests.Llm;
using Geef.Sdk.Events;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Pipeline;

public sealed class AtelierPipelineEventTests
{
    private const string Briefing = "Schreibe einen kurzen Text über das Walking-Skeleton-Pattern.";

    [Fact]
    public async Task AtelierPipelineEmitsExpectedEvents()
    {
        var fakeClient = new FakeLlmClient();
        var resolver   = new TestLlmClientResolver(fakeClient);
        var sink       = new CountingEventSink();

        var runner = AtelierPipelineFactory.BuildWithProviders(
            new BriefingGroundingStep(),
            new ProfileBasedExecutor(SystemCrew.DefaultExecutorProfile, resolver),
            [
                new ProfileBasedReviewer(SystemCrew.BriefingFidelityProfile, resolver),
                new ProfileBasedReviewer(SystemCrew.ClarityProfile, resolver)
            ],
            new MarkdownFinalizer(),
            Options.Create(new ConvergenceOptions()),
            additionalSinks: [sink]);

        await runner.RunAsync(Briefing, CancellationToken.None);

        // Pipeline lifecycle
        Assert.Equal(1, sink.Get<PipelineStartedEvent>());
        Assert.Equal(1, sink.Get<PipelineCompletedEvent>());
        Assert.Equal(0, sink.Get<PipelineFailedEvent>());

        // Grounding — runs once
        Assert.Equal(1, sink.Get<GroundingStartedEvent>());
        Assert.Equal(1, sink.Get<GroundingCompletedEvent>());

        // Execution — once per iteration (2 iterations)
        Assert.Equal(2, sink.Get<ExecutionStartedEvent>());
        Assert.Equal(2, sink.Get<ExecutionCompletedEvent>());
    }
}
