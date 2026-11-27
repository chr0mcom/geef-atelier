using System.Net;
using System.Text;
using Geef.Atelier.Application.Providers;
using Geef.Atelier.Core.Domain.Providers;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Tests.Domain.Crew;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Llm;

public sealed class LlmClientResolverTests
{
    private static IOptions<LlmOptions> MakeOptions(
        Dictionary<string, LlmOptions.ProviderConfig>? providers = null) =>
        Options.Create(new LlmOptions
        {
            ProvidersFallback = providers ?? new()
            {
                ["openrouter"] = new() { Endpoint = "https://openrouter.ai/api/v1", ApiKey = "key1" },
                ["claude-cli"] = new() { Endpoint = "http://cli-proxy:8090/v1/claude", ApiKey = "" },
                ["codex-cli"]  = new() { Endpoint = "http://cli-proxy:8090/v1/codex",  ApiKey = "" }
            }
        });

    private static IHttpClientFactory MakeFactory()
    {
        var handler = new AlwaysOkHandler();
        var factory = new FakeHttpClientFactory(new HttpClient(handler));
        return factory;
    }

    /// <summary>
    /// Returns a scope factory whose <see cref="IProviderService"/> always returns null,
    /// forcing the resolver to fall back to <see cref="LlmOptions.ProvidersFallback"/>.
    /// </summary>
    private static IServiceScopeFactory MakeScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProviderService>(new FakeProviderService(null));
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IServiceScopeFactory>();
    }

    [Fact]
    public void ForProfile_ThrowsWhenProviderNotConfigured()
    {
        var opts = MakeOptions(new() { ["openrouter"] = new() { Endpoint = "https://openrouter.ai/api/v1", ApiKey = "" } });
        var resolver = new LlmClientResolver(MakeFactory(), opts, MakeScopeFactory());

        Assert.Throws<InvalidOperationException>(() => resolver.ForProfile("missing-provider", "some-model", null));
    }

    [Fact]
    public void ForProfile_FallsBackToDefaultMaxTokens_WhenNotSet()
    {
        var resolver = new LlmClientResolver(MakeFactory(), MakeOptions(), MakeScopeFactory());

        var (_, _, maxTokens) = resolver.ForProfile("claude-cli", "claude-sonnet-latest", null);

        Assert.Equal(16384, maxTokens);
    }

    [Fact]
    public void ForProfile_ResolvesClaudeCliProvider()
    {
        var resolver = new LlmClientResolver(MakeFactory(), MakeOptions(), MakeScopeFactory());

        var (client, model, maxTokens) = resolver.ForProfile("claude-cli", "claude-opus-4.8", 4096);

        Assert.NotNull(client);
        Assert.Equal("claude-opus-4-8", model); // dots normalized to dashes for claude-cli
        Assert.Equal(4096, maxTokens);
    }

    [Fact]
    public void ForProfile_ResolvesCodexCliProvider()
    {
        var resolver = new LlmClientResolver(MakeFactory(), MakeOptions(), MakeScopeFactory());

        var (client, model, maxTokens) = resolver.ForProfile("codex-cli", "gpt-4o", 2048);

        Assert.NotNull(client);
        Assert.Equal("gpt-4o", model);
        Assert.Equal(2048, maxTokens);
    }

    // --- helpers ---

    private sealed class AlwaysOkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new StringContent("{}", Encoding.UTF8, "application/json");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
