using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Tests.Llm;
using Geef.Sdk.Events;
using Geef.Sdk.Exceptions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Pipeline;

public sealed class AtelierPipelineCriticalAbortTests
{
    private const string Briefing = "Schreibe einen kurzen Text über das Walking-Skeleton-Pattern.";

    [Fact]
    public async Task AtelierPipelineAbortsOnCriticalFinding()
    {
        var criticalClient = new CriticalFakeLlmClient();
        var resolver       = new TestLlmClientResolver(criticalClient);
        var sink           = new CountingEventSink();

        var runner = AtelierPipelineFactory.BuildWithProviders(
            new BriefingGroundingStep(),
            new ProfileBasedExecutor(SystemCrew.DefaultExecutorProfile, resolver),
            [
                new ProfileBasedReviewer(SystemCrew.BriefingFidelityProfile, resolver),
                new ProfileBasedReviewer(SystemCrew.ClarityProfile, resolver)
            ],
            new MarkdownFinalizer(),
            Options.Create(new ConvergenceOptions { AbortOnCritical = true }),
            additionalSinks: [sink]);

        // AbortOnCritical=true causes the SDK to throw ConvergenceFailedException, not return Success=false.
        var ex = await Assert.ThrowsAsync<ConvergenceFailedException>(
            () => runner.RunAsync(Briefing, CancellationToken.None));

        Assert.Contains("AbortCriticalBlocker", ex.Message);
        Assert.Equal(1, sink.Get<PipelineFailedEvent>());
        Assert.Equal(0, sink.Get<PipelineCompletedEvent>());
        Assert.Equal(0, sink.Get<FinalizeStartedEvent>());
    }
}
