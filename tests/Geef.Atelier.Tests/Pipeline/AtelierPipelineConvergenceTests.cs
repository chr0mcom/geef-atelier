using Geef.Atelier.Core.Domain;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Tests.Llm;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Geef.Atelier.Tests.Pipeline;

public sealed class AtelierPipelineConvergenceTests(ITestOutputHelper output)
{
    private const string Briefing = "Schreibe einen kurzen Text über das Walking-Skeleton-Pattern.";

    [Fact]
    public async Task AtelierPipelineConvergesWithMockClient()
    {
        var options = Options.Create(new LlmOptions
        {
            ApiKey       = "fake-key-for-tests",
            DefaultModel = "fake-model"
        });
        var fakeClient = new FakeLlmClient();
        var outputSink = new OutputEventSink(output);

        var runner = AtelierPipelineFactory.BuildWithProviders(
            new BriefingGroundingStep(),
            new LlmExecutionStep(fakeClient, options),
            [
                new LlmReviewer("BriefingTreueReviewer", AtelierSystemPrompts.BriefingTreue, fakeClient, options),
                new LlmReviewer("KlarheitReviewer",       AtelierSystemPrompts.Klarheit,      fakeClient, options)
            ],
            new MarkdownFinalizer(),
            additionalSinks: [outputSink]);

        var result = await runner.RunAsync(Briefing, CancellationToken.None);

        Assert.True(result.Success);
        Assert.IsType<FinalizedDocument>(result.Output);
        Assert.Equal(2, result.TotalIterations);
        Assert.Equal(2, result.Output.IterationCount);
        Assert.Contains("iteration 2", result.Output.Markdown, StringComparison.OrdinalIgnoreCase);
    }
}
