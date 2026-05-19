using System.Net;
using Geef.Atelier.Application.Providers;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Providers;
using Geef.Atelier.Infrastructure.Crew;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

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

    private ModelCatalog BuildCatalog(HttpMessageHandler handler, Provider? provider = null)
    {
        var resolvedProvider = provider ?? SystemProviders.OpenRouter;
        var factory = new FakeHttpClientFactory(new HttpClient(handler));
        var scopeFactory = BuildScopeFactory(resolvedProvider);
        return new ModelCatalog(factory, _cache, scopeFactory, NullLogger<ModelCatalog>.Instance);
    }

    private static IServiceScopeFactory BuildScopeFactory(Provider? provider)
    {
        var fakeService = new FakeProviderService(provider);
        var services = new ServiceCollection();
        services.AddSingleton<IProviderService>(fakeService);
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IServiceScopeFactory>();
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
        var handler = FakeHttpHandler.Fail(HttpStatusCode.NotFound);
        var scopeFactory = BuildScopeFactory(null); // no provider returned
        var factory = new FakeHttpClientFactory(new HttpClient(handler));
        var catalog = new ModelCatalog(factory, _cache, scopeFactory, NullLogger<ModelCatalog>.Instance);

        var models = await catalog.ListModelsAsync("openrouter");

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
        var models = StaticModelFallback.ForClaudeCli;
        Assert.Contains(models, m => m.Id.Contains("claude-opus"));
        Assert.True(models.All(m => m.IsRecommended == m.IsRecommended));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task StaticFallback_For_UnknownProvider_ReturnsEmpty()
    {
        var models = StaticModelFallback.For("unknown-xyz");
        Assert.Empty(models);
        await Task.CompletedTask;
    }
}

// ---------------------------------------------------------------------------
// Test helpers
// ---------------------------------------------------------------------------

internal sealed class FakeHttpHandler(HttpStatusCode statusCode, string? body) : HttpMessageHandler
{
    public int CallCount { get; private set; }

    public static FakeHttpHandler Ok(string body) => new(HttpStatusCode.OK, body);
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

internal sealed class FakeProviderService(Provider? provider) : IProviderService
{
    public Task<IReadOnlyList<Provider>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Provider>>(provider is not null ? [provider] : []);

    public Task<Provider?> GetByNameAsync(string name, CancellationToken ct = default)
        => Task.FromResult(provider?.Name == name ? provider : null);

    public Task<Provider> CreateCustomAsync(Provider p, CancellationToken ct = default)
        => Task.FromResult(p);

    public Task<Provider> UpdateCustomAsync(string name, Provider p, CancellationToken ct = default)
        => Task.FromResult(p);

    public Task DeleteCustomAsync(string name, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task SetActiveAsync(string name, bool isActive, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<ConnectionTestResult> TestConnectionAsync(string name, CancellationToken ct = default)
        => Task.FromResult(new ConnectionTestResult(true, 0, null, null));
}
