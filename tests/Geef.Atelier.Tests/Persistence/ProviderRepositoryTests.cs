using System.Text.Json;
using Geef.Atelier.Core.Domain.Providers;
using Geef.Atelier.Infrastructure.Persistence.Providers;

namespace Geef.Atelier.Tests.Persistence;

[Collection("Postgres")]
public sealed class ProviderRepositoryTests(PostgresFixture db) : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var ctx = db.NewContext();
        var custom = ctx.Providers.Where(p => !p.IsSystem).ToList();
        ctx.Providers.RemoveRange(custom);
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task CreateAndGet_RoundTrips()
    {
        await using var ctx = db.NewContext();
        var repo = new ProviderRepository(ctx);
        var provider = new Provider(
            "custom-test-repo",
            "Test Repo Provider",
            "A provider for roundtrip test",
            ProviderType.Http,
            new Dictionary<string, JsonElement>(),
            IsSystem: false,
            IsActive: true,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        await repo.CreateAsync(provider, default);
        var retrieved = await repo.GetByNameAsync("custom-test-repo", default);

        Assert.NotNull(retrieved);
        Assert.Equal("Test Repo Provider", retrieved.DisplayName);
        Assert.Equal(ProviderType.Http, retrieved.Type);
        Assert.False(retrieved.IsSystem);
        Assert.True(retrieved.IsActive);
    }

    [Fact]
    public async Task MigrationSeededSystemProviders()
    {
        await using var ctx = db.NewContext();
        var repo = new ProviderRepository(ctx);
        var providers = await repo.ListAsync(includeInactive: false, default);

        // At minimum the 3 CLI providers should be seeded by migration
        Assert.Contains(providers, p => p.Name == "openrouter" && p.IsSystem);
        Assert.Contains(providers, p => p.Name == "claude-cli" && p.IsSystem);
        Assert.Contains(providers, p => p.Name == "codex-cli" && p.IsSystem);
    }

    [Fact]
    public async Task SetActiveAsync_UpdatesStatus()
    {
        await using var ctx = db.NewContext();
        var repo = new ProviderRepository(ctx);
        var provider = new Provider(
            "custom-active-test",
            "Active Test",
            "Test active toggle",
            ProviderType.Http,
            new Dictionary<string, JsonElement>(),
            IsSystem: false,
            IsActive: true,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        await repo.CreateAsync(provider, default);
        await repo.SetActiveAsync("custom-active-test", false, default);

        var retrieved = await repo.GetByNameAsync("custom-active-test", default);
        Assert.False(retrieved!.IsActive);
    }

    [Fact]
    public async Task ListAsync_ExcludesInactiveWhenFilterApplied()
    {
        await using var ctx = db.NewContext();
        var repo = new ProviderRepository(ctx);
        var inactive = new Provider(
            "custom-inactive-list-test",
            "Inactive Provider",
            "Should be filtered",
            ProviderType.Http,
            new Dictionary<string, JsonElement>(),
            IsSystem: false,
            IsActive: false,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        await repo.CreateAsync(inactive, default);
        var activeOnly = await repo.ListAsync(includeInactive: false, default);

        Assert.DoesNotContain(activeOnly, p => p.Name == "custom-inactive-list-test");
    }

    [Fact]
    public async Task ListAsync_IncludesInactiveWhenRequested()
    {
        await using var ctx = db.NewContext();
        var repo = new ProviderRepository(ctx);
        var inactive = new Provider(
            "custom-inactive-all-test",
            "Inactive All",
            "Should appear with includeInactive",
            ProviderType.Http,
            new Dictionary<string, JsonElement>(),
            IsSystem: false,
            IsActive: false,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        await repo.CreateAsync(inactive, default);
        var all = await repo.ListAsync(includeInactive: true, default);

        Assert.Contains(all, p => p.Name == "custom-inactive-all-test");
    }

    [Fact]
    public async Task UpdateAsync_ChangesDisplayName()
    {
        await using var ctx = db.NewContext();
        var repo = new ProviderRepository(ctx);
        var provider = new Provider(
            "custom-update-test",
            "Original Name",
            "desc",
            ProviderType.Http,
            new Dictionary<string, JsonElement>(),
            IsSystem: false,
            IsActive: true,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        await repo.CreateAsync(provider, default);
        var updated = provider with { DisplayName = "Updated Name" };
        await repo.UpdateAsync(updated, default);

        var retrieved = await repo.GetByNameAsync("custom-update-test", default);
        Assert.Equal("Updated Name", retrieved!.DisplayName);
    }

    [Fact]
    public async Task DeleteAsync_RemovesProvider()
    {
        await using var ctx = db.NewContext();
        var repo = new ProviderRepository(ctx);
        var provider = new Provider(
            "custom-delete-test",
            "Delete Me",
            "desc",
            ProviderType.Http,
            new Dictionary<string, JsonElement>(),
            IsSystem: false,
            IsActive: true,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        await repo.CreateAsync(provider, default);
        await repo.DeleteAsync("custom-delete-test", default);

        var retrieved = await repo.GetByNameAsync("custom-delete-test", default);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task GetByNameAsync_ReturnsNullForUnknownName()
    {
        await using var ctx = db.NewContext();
        var repo = new ProviderRepository(ctx);

        var retrieved = await repo.GetByNameAsync("this-does-not-exist", default);

        Assert.Null(retrieved);
    }

    [Fact]
    public async Task CliProvider_RoundTrips_WithCliType()
    {
        await using var ctx = db.NewContext();
        var repo = new ProviderRepository(ctx);
        var provider = new Provider(
            "custom-cli-roundtrip",
            "CLI Roundtrip",
            "CLI provider test",
            ProviderType.Cli,
            new Dictionary<string, JsonElement>(),
            IsSystem: false,
            IsActive: true,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        await repo.CreateAsync(provider, default);
        var retrieved = await repo.GetByNameAsync("custom-cli-roundtrip", default);

        Assert.NotNull(retrieved);
        Assert.Equal(ProviderType.Cli, retrieved.Type);
    }
}
