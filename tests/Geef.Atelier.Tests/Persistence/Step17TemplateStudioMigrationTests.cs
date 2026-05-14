using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Geef.Atelier.Tests.Persistence;

/// <summary>
/// Verifies that the Step17TemplateStudio migration applies cleanly and produces the expected schema.
/// </summary>
[Trait("Category", "Integration")]
public sealed class Step17TemplateStudioMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private AtelierDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AtelierDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new AtelierDbContext(options);
    }

    [Fact]
    public async Task Step17Migration_AppliesCleanly_NoPendingMigrationsAfter()
    {
        await using var context = NewContext();
        await context.Database.MigrateAsync();

        var pending = await context.Database.GetPendingMigrationsAsync();
        Assert.Empty(pending);
    }

    [Fact]
    public async Task Step17Migration_CreatesTemplateStudioAnalysesTable_WithZeroRows()
    {
        await using var context = NewContext();
        await context.Database.MigrateAsync();

        // Querying the DbSet proves the table was created and EF mapping is correct
        var count = await context.TemplateStudioAnalyses.CountAsync();
        Assert.Equal(0, count);
    }
}
