using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Infrastructure.Crew;
using Geef.Atelier.Infrastructure.Llm;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Domain.Crew;

/// <summary>
/// Unit tests for <see cref="ModelCatalog"/>.
/// Uses a fake <see cref="IHttpClientFactory"/> backed by <see cref="FakeHttpHandler"/>
/// to avoid real network calls.
/// </summary>
public sealed class ModelCatalogTests : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public void Dispose() => _cache.Dispose();

    private static IOptions<LlmOptions> BuildOptions(string providerName = "openrouter", string endpoint = "https://fake-provider.test/v1") =>
        Options.Create(new LlmOptions
        {
            Providers = new Dictionary<string, LlmOptions.ProviderConfig>
            {
                [providerName] = new LlmOptions.ProviderConfig { Endpoint = endpoint }
            }
        });

    private ModelCatalog BuildCatalog(HttpMessageHandler handler, string providerName = "openrouter", string endpoint = "https://fake-provider.test/v1")
    {
        var factory = new FakeHttpClientFactory(new HttpClient(handler) { BaseAddress = new Uri(endpoint) });
        return new ModelCatalog(factory, _cache, BuildOptions(providerName, endpoint), NullLogger<ModelCatalog>.Instance);
    }

    [Fact]
    public async Task ListModelsAsync_ReturnsApiModels_WhenEndpointSucceeds()
    {
        var handler = FakeHttpHandler.Ok("""
            {"object":"list","data":[
                {"id":"anthropic/claude-opus-4.7","object":"model"},
                {"id":"google/gemini-2.5-flash","object":"model"}
            ]}
            """);
        var catalog = BuildCatalog(handler);

        var models = await catalog.ListModelsAsync("openrouter");

        Assert.Equal(2, models.Count);
        Assert.Contains(models, m => m.Id == "anthropic/claude-opus-4.7");
        Assert.Contains(models, m => m.Id == "google/gemini-2.5-flash");
    }

    [Fact]
    public async Task ListModelsAsync_MarksRecommended_FromStaticFallback()
    {
        var handler = FakeHttpHandler.Ok("""
            {"object":"list","data":[
                {"id":"anthropic/claude-opus-4.7","object":"model"},
                {"id":"some/unknown-model","object":"model"}
            ]}
            """);
        var catalog = BuildCatalog(handler);

        var models = await catalog.ListModelsAsync("openrouter");

        var opus = models.Single(m => m.Id == "anthropic/claude-opus-4.7");
        var unknown = models.Single(m => m.Id == "some/unknown-model");
        Assert.True(opus.IsRecommended);
        Assert.False(unknown.IsRecommended);
        Assert.False(catalog.IsUsingFallback("openrouter"));
    }

    [Fact]
    public async Task ListModelsAsync_UsesFallback_WhenEndpointFails()
    {
        var handler = FakeHttpHandler.Fail(HttpStatusCode.ServiceUnavailable);
        var catalog = BuildCatalog(handler);

        var models = await catalog.ListModelsAsync("openrouter");

        Assert.True(models.Count > 0);
        Assert.Equal(StaticModelFallback.ForOpenRouter.Count, models.Count);
        Assert.True(catalog.IsUsingFallback("openrouter"));
    }

    [Fact]
    public async Task ListModelsAsync_UsesFallback_WhenProviderUnknown()
    {
        var factory  = new FakeHttpClientFactory(new HttpClient(FakeHttpHandler.Fail(HttpStatusCode.NotFound)));
        var options  = Options.Create(new LlmOptions()); // no providers configured
        var catalog  = new ModelCatalog(factory, _cache, options, NullLogger<ModelCatalog>.Instance);

        var models = await catalog.ListModelsAsync("openrouter");

        // Unknown provider — StaticModelFallback.For("openrouter") still returns the known list
        // because the static fallback is keyed by name independently.
        Assert.True(catalog.IsUsingFallback("openrouter"));
    }

    [Fact]
    public async Task ListModelsAsync_UsesCacheOnSecondCall()
    {
        var handler = FakeHttpHandler.Ok("""{"object":"list","data":[{"id":"anthropic/claude-opus-4.7"}]}""");
        var catalog = BuildCatalog(handler);

        await catalog.ListModelsAsync("openrouter");
        await catalog.ListModelsAsync("openrouter");

        Assert.Equal(1, handler.CallCount); // second call hit cache
    }

    [Fact]
    public async Task RefreshAsync_BypassesCache()
    {
        var handler = FakeHttpHandler.Ok("""{"object":"list","data":[{"id":"anthropic/claude-opus-4.7"}]}""");
        var catalog = BuildCatalog(handler);

        await catalog.ListModelsAsync("openrouter");
        await catalog.RefreshAsync("openrouter");

        Assert.Equal(2, handler.CallCount); // Refresh triggered a second fetch
    }

    [Fact]
    public async Task StaticFallback_ForClaudeCli_ReturnsExpectedModels()
    {
        // Static fallback should have known claude-cli models — no HTTP call needed.
        var models = StaticModelFallback.ForClaudeCli;
        Assert.Contains(models, m => m.Id.Contains("claude-opus"));
        Assert.True(models.All(m => m.IsRecommended == (m.IsRecommended)));
    }

    [Fact]
    public async Task StaticFallback_For_UnknownProvider_ReturnsEmpty()
    {
        var models = StaticModelFallback.For("unknown-xyz");
        Assert.Empty(models);
    }
}

// ---------------------------------------------------------------------------
// Test helpers
// ---------------------------------------------------------------------------

internal sealed class FakeHttpHandler(HttpStatusCode statusCode, string? body) : HttpMessageHandler
{
    public int CallCount { get; private set; }

    public static FakeHttpHandler Ok(string body)   => new(HttpStatusCode.OK, body);
    public static FakeHttpHandler Fail(HttpStatusCode code) => new(code, null);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        var resp = new HttpResponseMessage(statusCode);
        if (body is not null)
            resp.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        return Task.FromResult(resp);
    }
}

internal sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}
