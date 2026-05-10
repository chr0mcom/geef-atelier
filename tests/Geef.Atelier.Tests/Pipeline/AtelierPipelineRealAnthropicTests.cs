using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Pipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Pipeline;

public sealed class AtelierPipelineRealAnthropicTests
{
    private const string Briefing = "Schreibe einen kurzen Text (ca. 150 Wörter) über das Walking-Skeleton-Pattern in der Softwareentwicklung.";

    [Fact]
    public async Task AtelierPipelineRunsAgainstRealAnthropic()
    {
        var apiKey = Environment.GetEnvironmentVariable("Anthropic__ApiKey");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // No API key available. Set Anthropic__ApiKey env-var to run this integration test.
            return;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Anthropic:ApiKey"]        = apiKey,
                ["Anthropic:ExecutorModel"] = "claude-opus-4-7",
                ["Anthropic:ReviewerModel"] = "claude-opus-4-7"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAnthropicClient(configuration);

        await using var provider = services.BuildServiceProvider();
        var client  = provider.GetRequiredService<IAnthropicClient>();
        var options = provider.GetRequiredService<IOptions<AnthropicOptions>>();
        var sink    = new CountingEventSink();

        var runner = AtelierPipelineFactory.Build(client, options, additionalSinks: [sink]);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(300));
        var result    = await runner.RunAsync(Briefing, cts.Token);

        Assert.True(result.Success, $"Pipeline failed after {result.TotalIterations} iteration(s).");
        Assert.NotNull(result.Output);
        Assert.NotEmpty(result.Output.Markdown);
        Assert.True(result.TotalIterations >= 1);
        Assert.True(result.Output.IterationCount >= 1);
    }
}
