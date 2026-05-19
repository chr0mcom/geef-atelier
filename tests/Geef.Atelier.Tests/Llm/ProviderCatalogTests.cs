using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Providers;
using Geef.Atelier.Core.Domain.Providers;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Tests.Domain.Crew;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Llm;

public sealed class ProviderCatalogTests
{
    private static IServiceScopeFactory BuildScopeFactory(params Provider[] providers)
    {
        var fakeService = new FakeMultiProviderService(providers);
        var services = new ServiceCollection();
        services.AddSingleton<IProviderService>(fakeService);
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IServiceScopeFactory>();
    }

    [Fact]
    public void ListProviders_ReturnsProviders_WithDisplayNames()
    {
        var catalog = new ProviderCatalog(BuildScopeFactory(
            SystemProviders.OpenRouter,
            SystemProviders.ClaudeCli,
            SystemProviders.CodexCli));

        var providers = catalog.ListProviders();

        Assert.Equal(3, providers.Count);

        var byName = providers.ToDictionary(p => p.Name);
        Assert.True(byName.ContainsKey("openrouter"));
        Assert.True(byName.ContainsKey("claude-cli"));
        Assert.True(byName.ContainsKey("codex-cli"));

        Assert.Equal(SystemProviders.OpenRouter.DisplayName, byName["openrouter"].DisplayName);
        Assert.Equal(SystemProviders.ClaudeCli.DisplayName, byName["claude-cli"].DisplayName);
        Assert.Equal(SystemProviders.CodexCli.DisplayName, byName["codex-cli"].DisplayName);
    }

    [Fact]
    public void ListProviders_IsSortedAlphabetically()
    {
        var catalog = new ProviderCatalog(BuildScopeFactory(
            SystemProviders.OpenRouter,
            SystemProviders.CodexCli,
            SystemProviders.ClaudeCli));

        var names = catalog.ListProviders().Select(p => p.Name).ToList();

        Assert.Equal(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(), names);
    }

    [Fact]
    public void ListProviders_ReturnsEmpty_WhenNoProvidersConfigured()
    {
        var catalog = new ProviderCatalog(BuildScopeFactory());

        Assert.Empty(catalog.ListProviders());
    }

    [Fact]
    public void ProviderInfo_IsValueEquality()
    {
        var a = new ProviderInfo("claude-cli", "Claude Code CLI");
        var b = new ProviderInfo("claude-cli", "Claude Code CLI");

        Assert.Equal(a, b);
    }
}

// ---------------------------------------------------------------------------
// Test helper — returns a configurable list of providers
// ---------------------------------------------------------------------------

internal sealed class FakeMultiProviderService(Provider[] providers) : IProviderService
{
    public Task<IReadOnlyList<Provider>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Provider>>(providers.ToList());

    public Task<Provider?> GetByNameAsync(string name, CancellationToken ct = default)
        => Task.FromResult(providers.FirstOrDefault(p => p.Name == name));

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
