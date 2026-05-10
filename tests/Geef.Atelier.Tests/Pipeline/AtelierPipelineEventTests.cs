using Geef.Atelier.Core.Domain;
using Geef.Atelier.Infrastructure.Llm;
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
        var options = Options.Create(new AnthropicOptions
        {
            ApiKey        = "fake-key-for-tests",
            ExecutorModel = "fake-model",
            ReviewerModel = "fake-model"
        });
        var fakeClient = new FakeAnthropicClient();
        var sink       = new CountingEventSink();

        var runner = AtelierPipelineFactory.BuildWithProviders(
            new BriefingGroundingStep(),
            new LlmExecutionStep(fakeClient, options),
            [
                new LlmReviewer("BriefingTreueReviewer", AtelierSystemPrompts.BriefingTreue, fakeClient, options),
                new LlmReviewer("KlarheitReviewer",       AtelierSystemPrompts.Klarheit,      fakeClient, options)
            ],
            new MarkdownFinalizer(),
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

        // Reviewers — 2 reviewers × 2 iterations
        Assert.Equal(4, sink.Get<ReviewerStartedEvent>());
        Assert.Equal(4, sink.Get<ReviewerCompletedEvent>());

        // Evaluation — iter 1 rejected (findings), iter 2 approved (no findings)
        Assert.Equal(1, sink.Get<EvaluationRejectedEvent>());
        Assert.Equal(1, sink.Get<EvaluationApprovedEvent>());

        // Finalize — runs once
        Assert.Equal(1, sink.Get<FinalizeStartedEvent>());
        Assert.Equal(1, sink.Get<FinalizeCompletedEvent>());
    }
}
