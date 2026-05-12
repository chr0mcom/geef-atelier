using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Sdk.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Pipeline;

public sealed class AtelierPipelineRunsAgainstOpenRouterTests
{
    private const string Briefing = "Schreibe einen kurzen Text (ca. 150 Wörter) über das Walking-Skeleton-Pattern in der Softwareentwicklung.";

    [Fact]
    public async Task AtelierPipelineRunsAgainstOpenRouter()
    {
        var apiKey = Environment.GetEnvironmentVariable("Llm__ApiKey");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // No API key available. Set Llm__ApiKey env-var to run this integration test.
            return;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:DefaultProvider"]                            = "openrouter",
                ["Llm:Providers:openrouter:Endpoint"]              = "https://openrouter.ai/api/v1",
                ["Llm:Providers:openrouter:ApiKey"]                = apiKey,
                ["Llm:Actors:Executor:Provider"]                   = "openrouter",
                ["Llm:Actors:Executor:Model"]                      = "anthropic/claude-opus-4.7",
                ["Llm:Actors:BriefingTreueReviewer:Provider"]      = "openrouter",
                ["Llm:Actors:BriefingTreueReviewer:Model"]         = "anthropic/claude-opus-4.7",
                ["Llm:Actors:KlarheitReviewer:Provider"]           = "openrouter",
                ["Llm:Actors:KlarheitReviewer:Model"]              = "anthropic/claude-opus-4.7"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmClient(configuration).AddStandardResilienceHandler();

        await using var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<ILlmClientResolver>();
        var sink     = new CountingEventSink();

        var runner = AtelierPipelineFactory.Build(resolver, Options.Create(new ConvergenceOptions()), additionalSinks: [sink]);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(300));
        var result    = await runner.RunAsync(Briefing, cts.Token);

        Assert.True(result.Success, $"Pipeline failed after {result.TotalIterations} iteration(s).");
        Assert.NotNull(result.Output);
        Assert.NotEmpty(result.Output.Markdown);
        Assert.True(result.TotalIterations >= 1);
        Assert.True(result.Output.IterationCount >= 1);
        Assert.True(sink.TotalEvents > 0);
    }

    [Fact]
    public async Task HadwigerNelson_DoesNotAbortWithCriticalBlocker()
    {
        var apiKey = Environment.GetEnvironmentVariable("Llm__ApiKey");
        if (string.IsNullOrWhiteSpace(apiKey))
            return; // No API key available

        const string hadwigerNelsonBriefing = """
            Beantworte folgende Frage präzise und strukturiert (ca. 200-250 Wörter):
            Wie viele Farben sind mindestens notwendig, um die gesamte Ebene so einzufärben,
            dass je zwei Punkte mit Abstand 1 unterschiedlich gefärbt sind?
            Erkläre die bekannten Schranken und den aktuellen Stand der Forschung.
            """;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:DefaultProvider"]                            = "openrouter",
                ["Llm:Providers:openrouter:Endpoint"]              = "https://openrouter.ai/api/v1",
                ["Llm:Providers:openrouter:ApiKey"]                = apiKey,
                ["Llm:Actors:Executor:Provider"]                   = "openrouter",
                ["Llm:Actors:Executor:Model"]                      = "anthropic/claude-opus-4.7",
                ["Llm:Actors:BriefingTreueReviewer:Provider"]      = "openrouter",
                ["Llm:Actors:BriefingTreueReviewer:Model"]         = "anthropic/claude-opus-4.7",
                ["Llm:Actors:KlarheitReviewer:Provider"]           = "openrouter",
                ["Llm:Actors:KlarheitReviewer:Model"]              = "anthropic/claude-opus-4.7"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmClient(configuration).AddStandardResilienceHandler();

        await using var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<ILlmClientResolver>();
        var sink     = new CountingEventSink();

        var runner = AtelierPipelineFactory.Build(
            resolver,
            Options.Create(new ConvergenceOptions { AbortOnCritical = false }),
            additionalSinks: [sink]);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(300));
        try
        {
            var result = await runner.RunAsync(hadwigerNelsonBriefing, cts.Token);
            // Full convergence: best-case outcome.
            Assert.True(result.TotalIterations <= 3, $"Pipeline took {result.TotalIterations} iterations — expected <= 3.");
            Assert.NotNull(result.Output);
            Assert.NotEmpty(result.Output.Markdown);
        }
        catch (ConvergenceFailedException ex)
        {
            // AbortCriticalBlocker = the pre-PS2 bug (overzealous critical abort after 1 iteration).
            // StopMaxAttemptsReached = reviewers still strict, but no early abort — acceptable.
            Assert.DoesNotContain("AbortCriticalBlocker", ex.Message);
        }
    }
}
