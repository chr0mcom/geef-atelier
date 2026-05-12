using Geef.Atelier.Core.Domain;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Infrastructure.Pipeline;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Geef.Atelier.Tests.Pipeline;

public sealed class StubPipelineConvergenceTests(ITestOutputHelper output)
{
    private const string Briefing = "Schreib mir einen Test-Text über Walking-Skeleton-Pattern.";

    [Fact]
    public async Task StubPipelineRunsToConvergence()
    {
        var outputSink = new OutputEventSink(output);
        var runner = AtelierPipelineFactory.BuildWithProviders(
            new BriefingGroundingStep(),
            new StubExecutionStep(),
            [
                new StubReviewer("BriefingTreueStub",  Geef.Sdk.Results.FindingSeverity.Error,   "Stub finding: simulated briefing-coverage gap (will be cleared on next iteration)."),
                new StubReviewer("KlarheitStub",        Geef.Sdk.Results.FindingSeverity.Warning, "Stub finding: simulated clarity nit (will be cleared on next iteration).")
            ],
            new MarkdownFinalizer(),
            Options.Create(new ConvergenceOptions()),
            additionalSinks: [outputSink]);

        var result = await runner.RunAsync(Briefing, CancellationToken.None);

        Assert.True(result.Success);
        Assert.IsType<FinalizedDocument>(result.Output);
        Assert.Equal(2, result.TotalIterations);
        Assert.Equal(2, result.Output.IterationCount);
        Assert.Contains("DRAFT v2", result.Output.Markdown);
    }
}
