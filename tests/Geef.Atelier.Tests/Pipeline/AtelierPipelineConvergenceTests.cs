using Geef.Atelier.Core.Domain;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Profiles;
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
        var fakeClient = new FakeLlmClient();
        var resolver   = new TestLlmClientResolver(fakeClient);
        var outputSink = new OutputEventSink(output);

        var runner = AtelierPipelineFactory.BuildWithProviders(
            new BriefingGroundingStep(),
            new ProfileBasedExecutor(SystemCrew.DefaultExecutorProfile, resolver),
            [
                new ProfileBasedReviewer(SystemCrew.BriefingFidelityProfile, resolver),
                new ProfileBasedReviewer(SystemCrew.ClarityProfile, resolver)
            ],
            new MarkdownFinalizer(),
            Options.Create(new ConvergenceOptions()),
            additionalSinks: [outputSink]);

        var result = await runner.RunAsync(Briefing, CancellationToken.None);

        Assert.True(result.Success);
        Assert.IsType<FinalizedDocument>(result.Output);
        Assert.Equal(2, result.TotalIterations);
        Assert.Equal(2, result.Output.IterationCount);
        Assert.Contains("iteration 2", result.Output.Markdown, StringComparison.OrdinalIgnoreCase);
    }
}
