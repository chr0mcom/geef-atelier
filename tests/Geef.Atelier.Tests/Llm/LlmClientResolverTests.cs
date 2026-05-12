using System.Net;
using System.Text;
using Geef.Atelier.Infrastructure.Llm;
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
            Providers       = providers ?? new()
            {
                ["openrouter"] = new() { Endpoint = "https://openrouter.ai/api/v1", ApiKey = "key1" },
                ["cli"]        = new() { Endpoint = "http://cli-proxy:8090/v1",     ApiKey = "" }
            },
            Actors = actors ?? new()
            {
                ["Executor"]              = new() { Provider = "openrouter", Model = "claude-opus-4.7", MaxTokens = 8192 },
                ["BriefingTreueReviewer"] = new() { Provider = "cli",        Model = "claude-sonnet-4-5" },
            }
        });

    private static IHttpClientFactory MakeFactory()
    {
        // Returns a client with a mock handler that always 200-OKs.
        var handler = new AlwaysOkHandler();
        var factory = new FakeHttpClientFactory(new HttpClient(handler));
        return factory;
    }

    [Fact]
    public void ForActor_ReturnsConfiguredClientAndModel()
    {
        var resolver = new LlmClientResolver(MakeFactory(), MakeOptions());

        var (client, model, maxTokens) = resolver.ForActor("Executor");

        Assert.NotNull(client);
        Assert.Equal("claude-opus-4.7", model);
        Assert.Equal(8192, maxTokens);
    }

    [Fact]
    public void ForActor_FallsBackToDefaultMaxTokens_WhenNotSet()
    {
        var resolver = new LlmClientResolver(MakeFactory(), MakeOptions());

        var (_, _, maxTokens) = resolver.ForActor("BriefingTreueReviewer");

        Assert.Equal(4096, maxTokens); // DefaultMaxTokens
    }

    [Fact]
    public void ForActor_UsesDifferentProvidersPerActor()
    {
        var resolver = new LlmClientResolver(MakeFactory(), MakeOptions());

        // Both should resolve without exception even though they use different providers.
        var (clientA, _, _) = resolver.ForActor("Executor");
        var (clientB, _, _) = resolver.ForActor("BriefingTreueReviewer");

        Assert.NotNull(clientA);
        Assert.NotNull(clientB);
    }

    [Fact]
    public void ForActor_ThrowsOnUnknownActor()
    {
        var resolver = new LlmClientResolver(MakeFactory(), MakeOptions());

        Assert.Throws<InvalidOperationException>(() => resolver.ForActor("NonExistentActor"));
    }

    [Fact]
    public void ForActor_ThrowsWhenProviderNotConfigured()
    {
        var opts = MakeOptions(actors: new()
        {
            ["Executor"] = new() { Provider = "missing-provider", Model = "some-model" }
        });
        var resolver = new LlmClientResolver(MakeFactory(), opts);

        Assert.Throws<InvalidOperationException>(() => resolver.ForActor("Executor"));
    }

    [Fact]
    public void ForActor_UsesDefaultProviderWhenActorProviderEmpty()
    {
        var opts = MakeOptions(actors: new()
        {
            ["Executor"] = new() { Provider = "", Model = "some-model" }
        });
        var resolver = new LlmClientResolver(MakeFactory(), opts);

        // DefaultProvider is "openrouter" which is configured.
        var (client, model, _) = resolver.ForActor("Executor");
        Assert.NotNull(client);
        Assert.Equal("some-model", model);
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
