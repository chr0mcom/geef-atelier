using Geef.Atelier.Infrastructure.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Llm;

public sealed class LlmOptionsMultiProviderTests
{
    private static IOptions<LlmOptions> BuildOptions(string json)
    {
        var config = new ConfigurationBuilder()
            .AddJsonStream(new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
            .Build();

        var services = new ServiceCollection();
        services.Configure<LlmOptions>(config.GetSection("Llm"));
        return services.BuildServiceProvider().GetRequiredService<IOptions<LlmOptions>>();
    }

    [Fact]
    public void ParsesProvidersAndActors()
    {
        var opts = BuildOptions("""
            {
              "Llm": {
                "DefaultProvider": "openrouter",
                "DefaultMaxTokens": 4096,
                "Providers": {
                  "openrouter":  { "Endpoint": "https://openrouter.ai/api/v1",        "ApiKey": "key1" },
                  "claude-cli":  { "Endpoint": "http://cli-proxy:8090/v1/claude",     "ApiKey": "" },
                  "codex-cli":   { "Endpoint": "http://cli-proxy:8090/v1/codex",      "ApiKey": "" }
                },
                "Actors": {
                  "Executor": { "Provider": "claude-cli", "Model": "claude-sonnet-4-5", "MaxTokens": 8192 },
                  "BriefingTreueReviewer": { "Provider": "openrouter", "Model": "google/gemini-2.5-flash" }
                }
              }
            }
            """);

        Assert.Equal("openrouter", opts.Value.DefaultProvider);
        Assert.Equal(4096, opts.Value.DefaultMaxTokens);
        Assert.Equal(3, opts.Value.Providers.Count);
        Assert.Equal("https://openrouter.ai/api/v1", opts.Value.Providers["openrouter"].Endpoint);
        Assert.Equal("key1", opts.Value.Providers["openrouter"].ApiKey);
        Assert.Equal("http://cli-proxy:8090/v1/claude", opts.Value.Providers["claude-cli"].Endpoint);
        Assert.Equal("http://cli-proxy:8090/v1/codex", opts.Value.Providers["codex-cli"].Endpoint);

        Assert.Equal(2, opts.Value.Actors.Count);
        Assert.Equal("claude-cli", opts.Value.Actors["Executor"].Provider);
        Assert.Equal("claude-sonnet-4-5", opts.Value.Actors["Executor"].Model);
        Assert.Equal(8192, opts.Value.Actors["Executor"].MaxTokens);
        Assert.Equal("openrouter", opts.Value.Actors["BriefingTreueReviewer"].Provider);
        Assert.Null(opts.Value.Actors["BriefingTreueReviewer"].MaxTokens);
    }

    [Fact]
    public void DefaultsAreApplied()
    {
        var opts = BuildOptions("""{ "Llm": {} }""");

        Assert.Equal("openrouter", opts.Value.DefaultProvider);
        Assert.Equal(16384, opts.Value.DefaultMaxTokens);
        Assert.Empty(opts.Value.Providers);
        Assert.Empty(opts.Value.Actors);
    }
}
