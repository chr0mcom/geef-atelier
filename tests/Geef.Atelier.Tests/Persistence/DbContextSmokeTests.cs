using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Geef.Atelier.Tests.Persistence;

[Trait("Category", "Integration")]
public sealed class DbContextSmokeTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task CanConnectAndMigrateAgainstPostgres()
    {
        var options = new DbContextOptionsBuilder<AtelierDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using var context = new AtelierDbContext(options);
        await context.Database.MigrateAsync();

        Assert.True(await context.Database.CanConnectAsync());
        Assert.Equal(0, await context.Runs.CountAsync());
        Assert.Equal(0, await context.Iterations.CountAsync());
        Assert.Equal(0, await context.Findings.CountAsync());
        Assert.Equal(0, await context.Events.CountAsync());
    }
}
