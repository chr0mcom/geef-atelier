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
    public void ParsesProviders_UnderTheOlderSectionName()
    {
        var opts = BuildOptions("""
            {
              "Llm": {
                "DefaultMaxTokens": 4096,
                "Providers": {
                  "openrouter":  { "Endpoint": "https://openrouter.ai/api/v1",        "ApiKey": "key1" },
                  "claude-cli":  { "Endpoint": "http://cli-proxy:8090/v1/claude",     "ApiKey": "" },
                  "codex-cli":   { "Endpoint": "http://cli-proxy:8090/v1/codex",      "ApiKey": "" }
                }
              }
            }
            """);

        Assert.Equal(4096, opts.Value.DefaultMaxTokens);
        Assert.Equal(3, opts.Value.Providers.Count);
        Assert.Equal("https://openrouter.ai/api/v1", opts.Value.Providers["openrouter"].Endpoint);
        Assert.Equal("key1", opts.Value.Providers["openrouter"].ApiKey);
        Assert.Equal("http://cli-proxy:8090/v1/claude", opts.Value.Providers["claude-cli"].Endpoint);
        Assert.Equal("http://cli-proxy:8090/v1/codex", opts.Value.Providers["codex-cli"].Endpoint);
    }

    [Fact]
    public void DefaultsAreApplied()
    {
        var opts = BuildOptions("""{ "Llm": {} }""");

        Assert.Equal(16384, opts.Value.DefaultMaxTokens);
        Assert.Empty(opts.Value.Providers);
    }
}
