using Geef.Atelier.Core.Domain;
using Geef.Atelier.Infrastructure.Pipeline;
using Xunit.Abstractions;

namespace Geef.Atelier.Tests.Pipeline;

public sealed class StubPipelineConvergenceTests(ITestOutputHelper output)
{
    private const string Briefing = "Schreib mir einen Test-Text über Walking-Skeleton-Pattern.";

    [Fact]
    public async Task StubPipelineRunsToConvergence()
    {
        var outputSink = new OutputEventSink(output);
        var runner = StubPipelineFactory.Build(additionalSinks: [outputSink]);

        var result = await runner.RunAsync(Briefing, CancellationToken.None);

        Assert.True(result.Success);
        Assert.IsType<FinalizedDocument>(result.Output);
        Assert.Equal(2, result.TotalIterations);
        Assert.Equal(2, result.Output.IterationCount);
        Assert.Contains("DRAFT v2", result.Output.Markdown);
    }
}
