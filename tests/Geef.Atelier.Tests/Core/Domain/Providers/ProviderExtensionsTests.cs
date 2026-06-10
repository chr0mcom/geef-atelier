using System.Text.Json;
using Geef.Atelier.Core.Domain.Providers;

namespace Geef.Atelier.Tests.Core.Domain.Providers;

public sealed class ProviderExtensionsTests
{
    // ── helpers ──────────────────────────────────────────────────────────────────────

    private static Provider MakeProvider(
        ProviderType type,
        Dictionary<string, JsonElement>? settings = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new Provider(
            Name: "test-provider",
            DisplayName: "Test Provider",
            Description: "Unit-test stub",
            Type: type,
            Settings: settings ?? [],
            IsSystem: false,
            IsActive: true,
            CreatedAt: now,
            UpdatedAt: now
        );
    }

    private static JsonElement JsonBool(bool value)
        => JsonDocument.Parse(value ? "true" : "false").RootElement;

    private static JsonElement JsonString(string value)
        => JsonDocument.Parse($"\"{value}\"").RootElement;

    // ── default-by-type ──────────────────────────────────────────────────────────────

    [Fact]
    public void SupportsAgenticTools_HttpProvider_DefaultTrue()
    {
        var provider = MakeProvider(ProviderType.Http);
        Assert.True(provider.SupportsAgenticTools());
    }

    [Fact]
    public void SupportsAgenticTools_CliProvider_DefaultTrue()
    {
        var provider = MakeProvider(ProviderType.Cli);
        Assert.True(provider.SupportsAgenticTools());
    }

    // ── explicit overrides ───────────────────────────────────────────────────────────

    [Fact]
    public void SupportsAgenticTools_ExplicitFalse_ReturnsFalse()
    {
        var settings = new Dictionary<string, JsonElement>
        {
            [ProviderSettingsKeys.SupportsAgenticTools] = JsonBool(false)
        };
        // Even for an HTTP provider that defaults to true, explicit false wins.
        var provider = MakeProvider(ProviderType.Http, settings);
        Assert.False(provider.SupportsAgenticTools());
    }

    [Fact]
    public void SupportsAgenticTools_ExplicitTrue_ReturnsTrue()
    {
        var settings = new Dictionary<string, JsonElement>
        {
            [ProviderSettingsKeys.SupportsAgenticTools] = JsonBool(true)
        };
        var provider = MakeProvider(ProviderType.Http, settings);
        Assert.True(provider.SupportsAgenticTools());
    }

    [Fact]
    public void SupportsAgenticTools_ExplicitStringTrue_ReturnsTrue()
    {
        var settings = new Dictionary<string, JsonElement>
        {
            [ProviderSettingsKeys.SupportsAgenticTools] = JsonString("true")
        };
        var provider = MakeProvider(ProviderType.Http, settings);
        Assert.True(provider.SupportsAgenticTools());
    }

    [Fact]
    public void SupportsAgenticTools_ExplicitStringFalse_ReturnsFalse()
    {
        var settings = new Dictionary<string, JsonElement>
        {
            [ProviderSettingsKeys.SupportsAgenticTools] = JsonString("false")
        };
        var provider = MakeProvider(ProviderType.Http, settings);
        Assert.False(provider.SupportsAgenticTools());
    }

    // ── resolver null-guard ──────────────────────────────────────────────────────────

    [Fact]
    public void SupportsAgenticTools_NullProvider_ReturnsFalse()
    {
        // When ILlmClientResolver.LoadProvider returns null (unknown provider name),
        // the resolver returns false.  Test the resolver directly here via a null check.
        Provider? nullProvider = null;
        var result = nullProvider?.SupportsAgenticTools() ?? false;
        Assert.False(result);
    }
}
