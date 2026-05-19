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
        string defaultProvider = "openrouter",
        Dictionary<string, LlmOptions.ProviderConfig>? providers = null,
        Dictionary<string, LlmOptions.ActorConfig>? actors = null) =>
        Options.Create(new LlmOptions
        {
            DefaultProvider = defaultProvider,
            ProvidersFallback = providers ?? new()
            {
                ["openrouter"] = new() { Endpoint = "https://openrouter.ai/api/v1", ApiKey = "key1" },
                ["claude-cli"] = new() { Endpoint = "http://cli-proxy:8090/v1/claude", ApiKey = "" },
                ["codex-cli"]  = new() { Endpoint = "http://cli-proxy:8090/v1/codex",  ApiKey = "" }
            },
            Actors = actors ?? new()
            {
                ["Executor"]              = new() { Provider = "openrouter",  Model = "claude-opus-4.7", MaxTokens = 8192 },
                ["BriefingTreueReviewer"] = new() { Provider = "claude-cli",  Model = "claude-sonnet-4-5" },
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
    public void ForActor_ReturnsConfiguredClientAndModel()
    {
        var resolver = new LlmClientResolver(MakeFactory(), MakeOptions(), MakeScopeFactory());

        var (client, model, maxTokens) = resolver.ForActor("Executor");

        Assert.NotNull(client);
        Assert.Equal("claude-opus-4.7", model);
        Assert.Equal(8192, maxTokens);
    }

    [Fact]
    public void ForActor_FallsBackToDefaultMaxTokens_WhenNotSet()
    {
        var resolver = new LlmClientResolver(MakeFactory(), MakeOptions(), MakeScopeFactory());

        var (_, _, maxTokens) = resolver.ForActor("BriefingTreueReviewer");

        Assert.Equal(16384, maxTokens); // DefaultMaxTokens
    }

    [Fact]
    public void ForActor_UsesDifferentProvidersPerActor()
    {
        var resolver = new LlmClientResolver(MakeFactory(), MakeOptions(), MakeScopeFactory());

        var (clientA, _, _) = resolver.ForActor("Executor");
        var (clientB, _, _) = resolver.ForActor("BriefingTreueReviewer");

        Assert.NotNull(clientA);
        Assert.NotNull(clientB);
    }

    [Fact]
    public void ForActor_ThrowsOnUnknownActor()
    {
        var resolver = new LlmClientResolver(MakeFactory(), MakeOptions(), MakeScopeFactory());

        Assert.Throws<InvalidOperationException>(() => resolver.ForActor("NonExistentActor"));
    }

    [Fact]
    public void ForActor_ThrowsWhenProviderNotConfigured()
    {
        var opts = MakeOptions(
            providers: new() { ["openrouter"] = new() { Endpoint = "https://openrouter.ai/api/v1", ApiKey = "" } },
            actors: new() { ["Executor"] = new() { Provider = "missing-provider", Model = "some-model" } });
        var resolver = new LlmClientResolver(MakeFactory(), opts, MakeScopeFactory());

        Assert.Throws<InvalidOperationException>(() => resolver.ForActor("Executor"));
    }

    [Fact]
    public void ForActor_UsesDefaultProviderWhenActorProviderEmpty()
    {
        var opts = MakeOptions(actors: new()
        {
            ["Executor"] = new() { Provider = "", Model = "some-model" }
        });
        var resolver = new LlmClientResolver(MakeFactory(), opts, MakeScopeFactory());

        var (client, model, _) = resolver.ForActor("Executor");
        Assert.NotNull(client);
        Assert.Equal("some-model", model);
    }

    [Fact]
    public void ForProfile_ResolvesClaudeCliProvider()
    {
        var resolver = new LlmClientResolver(MakeFactory(), MakeOptions(), MakeScopeFactory());

        var (client, model, maxTokens) = resolver.ForProfile("claude-cli", "claude-opus-4.7", 4096);

        Assert.NotNull(client);
        Assert.Equal("claude-opus-4.7", model);
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
