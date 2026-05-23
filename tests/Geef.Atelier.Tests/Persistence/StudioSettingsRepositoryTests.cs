using Geef.Atelier.Core.Domain;
using Geef.Atelier.Infrastructure.Persistence.StudioSettings;

namespace Geef.Atelier.Tests.Persistence;

[Collection("Postgres")]
public sealed class StudioSettingsRepositoryTests(PostgresFixture db) : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var ctx = db.NewContext();
        var rows = ctx.StudioSettings.ToList();
        ctx.StudioSettings.RemoveRange(rows);
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task GetAsync_WhenEmpty_CreatesEmptyDefaultRow()
    {
        await using var ctx = db.NewContext();
        var repo = new StudioSettingsRepository(ctx);

        var settings = await repo.GetAsync(default);

        Assert.Equal("", settings.Provider);
        Assert.Equal("", settings.Model);
        Assert.Equal(0, settings.MaxTokens);
    }

    [Fact]
    public async Task Update_ThenGet_RoundTrips()
    {
        await using (var ctx = db.NewContext())
        {
            var repo = new StudioSettingsRepository(ctx);
            await repo.UpdateAsync(new StudioSettings
            {
                Provider = "codex-cli",
                Model = "gpt-5.5",
                MaxTokens = 20000,
            }, default);
        }

        await using (var ctx = db.NewContext())
        {
            var repo = new StudioSettingsRepository(ctx);
            var settings = await repo.GetAsync(default);

            Assert.Equal("codex-cli", settings.Provider);
            Assert.Equal("gpt-5.5", settings.Model);
            Assert.Equal(20000, settings.MaxTokens);
        }
    }

    [Fact]
    public async Task Update_IsSingleton_DoesNotCreateSecondRow()
    {
        await using var ctx = db.NewContext();
        var repo = new StudioSettingsRepository(ctx);

        await repo.UpdateAsync(new StudioSettings { Provider = "openrouter", Model = "a", MaxTokens = 1 }, default);
        await repo.UpdateAsync(new StudioSettings { Provider = "claude-cli", Model = "b", MaxTokens = 2 }, default);

        Assert.Equal(1, ctx.StudioSettings.Count());
        var settings = await repo.GetAsync(default);
        Assert.Equal("claude-cli", settings.Provider);
    }
}
