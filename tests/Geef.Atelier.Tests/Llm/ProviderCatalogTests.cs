using Geef.Atelier.Application.Crew;
using Geef.Atelier.Infrastructure.Llm;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Llm;

public sealed class ProviderCatalogTests
{
    private static IOptions<LlmOptions> MakeOptions(params string[] providerNames)
    {
        var providers = providerNames.ToDictionary(
            name => name,
            _ => new LlmOptions.ProviderConfig { Endpoint = "http://fake", ApiKey = "" });

        return Options.Create(new LlmOptions { Providers = providers });
    }

    [Fact]
    public void ListProviders_ReturnsThreeExpectedProviders_WithDisplayNames()
    {
        var catalog = new ProviderCatalog(MakeOptions("openrouter", "claude-cli", "codex-cli"));

        var providers = catalog.ListProviders();

        Assert.Equal(3, providers.Count);

        var byName = providers.ToDictionary(p => p.Name);
        Assert.True(byName.ContainsKey("openrouter"));
        Assert.True(byName.ContainsKey("claude-cli"));
        Assert.True(byName.ContainsKey("codex-cli"));

        Assert.Equal("OpenRouter (HTTP, pay-per-token)", byName["openrouter"].DisplayName);
        Assert.Equal("Claude (Subscription CLI)", byName["claude-cli"].DisplayName);
        Assert.Equal("Codex (Subscription CLI)", byName["codex-cli"].DisplayName);
    }

    [Fact]
    public void ListProviders_IsSortedAlphabetically()
    {
        var catalog = new ProviderCatalog(MakeOptions("openrouter", "codex-cli", "claude-cli"));

        var names = catalog.ListProviders().Select(p => p.Name).ToList();

        Assert.Equal(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(), names);
    }

    [Fact]
    public void ListProviders_UsesProviderNameAsDisplayName_ForUnknownProviders()
    {
        var catalog = new ProviderCatalog(MakeOptions("my-custom-provider"));

        var providers = catalog.ListProviders();

        Assert.Single(providers);
        Assert.Equal("my-custom-provider", providers[0].Name);
        Assert.Equal("my-custom-provider", providers[0].DisplayName);
    }

    [Fact]
    public void ListProviders_ReturnsEmpty_WhenNoProvidersConfigured()
    {
        var catalog = new ProviderCatalog(MakeOptions());

        Assert.Empty(catalog.ListProviders());
    }

    [Fact]
    public void ProviderInfo_IsValueEquality()
    {
        var a = new ProviderInfo("claude-cli", "Claude (Subscription CLI)");
        var b = new ProviderInfo("claude-cli", "Claude (Subscription CLI)");

        Assert.Equal(a, b);
    }
}
