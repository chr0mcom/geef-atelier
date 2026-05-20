using Geef.Atelier.Application.Providers;
using Geef.Atelier.Core.Domain.Providers;
using Geef.Atelier.Tests.Fakes;

namespace Geef.Atelier.Tests.Application;

public sealed class ProviderServiceTests
{
    [Fact]
    public async Task ListAsync_IncludesSystemProviders()
    {
        var service = BuildService();

        var providers = await service.ListAsync(includeInactive: false);

        Assert.Contains(providers, p => p.Name == "openrouter");
        Assert.Contains(providers, p => p.Name == "claude-cli");
    }

    [Fact]
    public async Task ListAsync_AlwaysIncludesAllSystemProviders()
    {
        var service = BuildService();

        var providers = await service.ListAsync(includeInactive: false);

        Assert.Equal(12, providers.Count(p => p.IsSystem));
    }

    [Fact]
    public async Task CreateCustomAsync_AddsCustomPrefix()
    {
        var service = BuildService();
        var provider = CreateTestProvider("myprovider");

        var result = await service.CreateCustomAsync(provider);

        Assert.Equal("custom-myprovider", result.Name);
    }

    [Fact]
    public async Task CreateCustomAsync_PreservesCustomPrefixWhenAlreadyPresent()
    {
        var service = BuildService();
        var provider = CreateTestProvider("custom-myprovider");

        var result = await service.CreateCustomAsync(provider);

        Assert.Equal("custom-myprovider", result.Name);
    }

    [Fact]
    public async Task CreateCustomAsync_SetsIsSystemFalse()
    {
        var service = BuildService();
        var provider = CreateTestProvider("myprovider");

        var result = await service.CreateCustomAsync(provider);

        Assert.False(result.IsSystem);
    }

    [Fact]
    public async Task CreateCustomAsync_ThrowsForSystemName()
    {
        var service = BuildService();
        var provider = CreateTestProvider("openrouter");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateCustomAsync(provider));
    }

    [Fact]
    public async Task DeleteCustomAsync_ThrowsForSystemProvider()
    {
        var service = BuildService();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteCustomAsync("openrouter"));
    }

    [Fact]
    public async Task DeleteCustomAsync_ThrowsWhenReferencedByProfile()
    {
        var repo = new FakeProviderRepository();
        var now = DateTimeOffset.UtcNow;
        await repo.CreateAsync(new Provider(
            "custom-used", "Used", "desc", ProviderType.Http,
            new Dictionary<string, System.Text.Json.JsonElement>(), false, true, now, now), default);
        repo.AddReference("custom-used");

        var service = new ProviderService(repo, new FakeHttpClientFactory());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteCustomAsync("custom-used"));
    }

    [Fact]
    public async Task DeleteCustomAsync_SucceedsForUnreferencedCustomProvider()
    {
        var service = BuildService();
        var created = await service.CreateCustomAsync(CreateTestProvider("todelete"));

        // Should not throw
        await service.DeleteCustomAsync(created.Name);

        var retrieved = await service.GetByNameAsync(created.Name);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task UpdateCustomAsync_PreservesCreatedAt()
    {
        var service = BuildService();
        var provider = CreateTestProvider("mytest");
        var created = await service.CreateCustomAsync(provider);
        var originalCreatedAt = created.CreatedAt;

        await Task.Delay(10); // ensure time passes
        var updated = await service.UpdateCustomAsync(created.Name, created with { DisplayName = "New Name" });

        Assert.Equal(originalCreatedAt, updated.CreatedAt);
        Assert.True(updated.UpdatedAt > originalCreatedAt);
    }

    [Fact]
    public async Task UpdateCustomAsync_ThrowsForSystemProvider()
    {
        var service = BuildService();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UpdateCustomAsync("openrouter", SystemProviders.OpenRouter));
    }

    [Fact]
    public async Task SetActiveAsync_TogglesCustomProvider()
    {
        var service = BuildService();
        var created = await service.CreateCustomAsync(CreateTestProvider("toggletest"));

        await service.SetActiveAsync(created.Name, false);

        var retrieved = await service.GetByNameAsync(created.Name);
        Assert.False(retrieved!.IsActive);
    }

    [Fact]
    public async Task SetActiveAsync_IsNoOpForSystemProvider()
    {
        var service = BuildService();

        // Must not throw — system providers silently skip.
        await service.SetActiveAsync("openrouter", false);
    }

    [Fact]
    public async Task GetByNameAsync_ReturnsSystemProvider()
    {
        var service = BuildService();

        var result = await service.GetByNameAsync("gemini-cli");

        Assert.NotNull(result);
        Assert.True(result.IsSystem);
        Assert.Equal("gemini-cli", result.Name);
    }

    [Fact]
    public async Task GetByNameAsync_ReturnsNullForUnknownName()
    {
        var service = BuildService();

        var result = await service.GetByNameAsync("does-not-exist");

        Assert.Null(result);
    }

    [Fact]
    public async Task TestConnectionAsync_CliProvider_ReturnsSyntheticSuccess()
    {
        var service = BuildService();

        var result = await service.TestConnectionAsync("claude-cli");

        Assert.True(result.Success);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ProviderService BuildService()
    {
        var repo = new FakeProviderRepository();
        var httpFactory = new FakeHttpClientFactory();
        return new ProviderService(repo, httpFactory);
    }

    private static Provider CreateTestProvider(string name) =>
        new(name, "Test", "Test provider", ProviderType.Http,
            new Dictionary<string, System.Text.Json.JsonElement>(), false, true,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
}
