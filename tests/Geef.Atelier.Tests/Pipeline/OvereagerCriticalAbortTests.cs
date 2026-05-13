using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Tests.Llm;
using Geef.Sdk.Events;
using Geef.Sdk.Exceptions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Pipeline;

public sealed class OvereagerCriticalAbortTests
{
    private const string Briefing = "Schreibe einen kurzen Text über Kalibrierung.";

    [Fact]
    public async Task Pipeline_WithAbortOnCriticalFalse_DoesNotAbortOnCriticalFindings()
    {
        // CriticalFakeLlmClient always returns critical findings.
        // With AbortOnCritical=false the pipeline must NOT throw with the AbortCriticalBlocker reason
        // (which signals an immediate early abort). It should instead run until MaxIterations and
        // then throw with StopMaxAttemptsReached — because it never converged, not because of early abort.
        var client   = new CriticalFakeLlmClient();
        var resolver = new TestLlmClientResolver(client);
        var sink     = new CountingEventSink();

        var runner = AtelierPipelineFactory.BuildWithProviders(
            new BriefingGroundingStep(),
            new ProfileBasedExecutor(SystemCrew.DefaultExecutorProfile, resolver),
            [
                new ProfileBasedReviewer(SystemCrew.BriefingFidelityProfile, resolver),
                new ProfileBasedReviewer(SystemCrew.ClarityProfile, resolver)
            ],
            new MarkdownFinalizer(),
            Options.Create(new ConvergenceOptions { AbortOnCritical = false, MaxIterations = 3 }),
            additionalSinks: [sink]);

        // The pipeline will still throw ConvergenceFailedException (never converges),
        // but the reason must be StopMaxAttemptsReached — NOT AbortCriticalBlocker.
        var ex = await Assert.ThrowsAsync<ConvergenceFailedException>(
            () => runner.RunAsync(Briefing, CancellationToken.None));

        // Must not be an early critical abort — the full MaxIterations were consumed.
        Assert.DoesNotContain("AbortCriticalBlocker", ex.Message);
        Assert.Contains("StopMaxAttemptsReached", ex.Message);

        // Exactly 3 iterations ran (MaxIterations=3), not 1 (which would indicate early abort).
        Assert.Equal(1, sink.Get<PipelineFailedEvent>());
        Assert.Equal(0, sink.Get<PipelineCompletedEvent>());
    }

    [Fact]
    public async Task Pipeline_WithAbortOnCriticalTrue_AbortsImmediately()
    {
        var client   = new CriticalFakeLlmClient();
        var resolver = new TestLlmClientResolver(client);
        var sink     = new CountingEventSink();

        var runnerWithSink = AtelierPipelineFactory.BuildWithProviders(
            new BriefingGroundingStep(),
            new ProfileBasedExecutor(SystemCrew.DefaultExecutorProfile, resolver),
            [
                new ProfileBasedReviewer(SystemCrew.BriefingFidelityProfile, resolver),
                new ProfileBasedReviewer(SystemCrew.ClarityProfile, resolver)
            ],
            new MarkdownFinalizer(),
            Options.Create(new ConvergenceOptions { AbortOnCritical = true }),
            additionalSinks: [sink]);

        var ex = await Assert.ThrowsAsync<ConvergenceFailedException>(
            () => runnerWithSink.RunAsync(Briefing, CancellationToken.None));

        Assert.Contains("AbortCriticalBlocker", ex.Message);
        Assert.Equal(1, sink.Get<PipelineFailedEvent>());
        Assert.Equal(0, sink.Get<PipelineCompletedEvent>());
    }
}
