using Geef.Atelier.Infrastructure.Llm;
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
        var options = Options.Create(new LlmOptions
        {
            ApiKey       = "fake-key-for-tests",
            DefaultModel = "fake-model"
        });
        var criticalClient = new CriticalFakeLlmClient();
        var sink           = new CountingEventSink();

        var runner = AtelierPipelineFactory.BuildWithProviders(
            new BriefingGroundingStep(),
            new LlmExecutionStep(criticalClient, options),
            [
                new LlmReviewer("BriefingTreueReviewer", AtelierSystemPrompts.BriefingTreue, criticalClient, options),
                new LlmReviewer("KlarheitReviewer",       AtelierSystemPrompts.Klarheit,      criticalClient, options)
            ],
            new MarkdownFinalizer(),
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
