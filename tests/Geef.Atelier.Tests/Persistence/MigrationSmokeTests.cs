using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Testcontainers.PostgreSql;

namespace Geef.Atelier.Tests.Persistence;

[Trait("Category", "Integration")]
public sealed class MigrationSmokeTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task MigrationAppliesCleanly()
    {
        var options = new DbContextOptionsBuilder<AtelierDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        await using var context = new AtelierDbContext(options);
        await context.Database.MigrateAsync();

        // Verify all four tables exist by checking pending migrations (none expected)
        var pending = await context.Database.GetPendingMigrationsAsync();
        Assert.Empty(pending);

        // Verify we can query each table without error (proves tables were created)
        var runCount = await context.Runs.CountAsync();
        var iterationCount = await context.Iterations.CountAsync();
        var findingCount = await context.Findings.CountAsync();
        var eventCount = await context.Events.CountAsync();

        Assert.Equal(0, runCount);
        Assert.Equal(0, iterationCount);
        Assert.Equal(0, findingCount);
        Assert.Equal(0, eventCount);
    }

    [Fact]
    public async Task MigrationIsIdempotent()
    {
        var options = new DbContextOptionsBuilder<AtelierDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        await using var context = new AtelierDbContext(options);

        // Running MigrateAsync twice must not throw
        await context.Database.MigrateAsync();
        await context.Database.MigrateAsync();

        var pending = await context.Database.GetPendingMigrationsAsync();
        Assert.Empty(pending);
    }
}
