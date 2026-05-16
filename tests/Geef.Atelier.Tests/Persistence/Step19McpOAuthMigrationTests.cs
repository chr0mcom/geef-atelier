using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Testcontainers.PostgreSql;

namespace Geef.Atelier.Tests.Persistence;

/// <summary>
/// Verifies that the Step19McpOAuth migration applies cleanly and creates all five OAuth tables.
/// </summary>
[Trait("Category", "Integration")]
public sealed class Step19McpOAuthMigrationTests : IAsyncLifetime
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
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new AtelierDbContext(options);
    }

    [Fact]
    public async Task Step19_AppliesCleanly_NoPendingMigrationsAfter()
    {
        await using var context = NewContext();
        await context.Database.MigrateAsync();

        var pending = await context.Database.GetPendingMigrationsAsync();
        Assert.Empty(pending);
    }

    [Fact]
    public async Task Step19_CreatesOAuthTables()
    {
        await using var ctx = NewContext();
        await ctx.Database.MigrateAsync();

        var conn = ctx.Database.GetDbConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();

        // Postgres table names are lowercase by convention — EF Core uses the entity name as-is
        // Check via information_schema which is case-insensitive via LOWER()
        cmd.CommandText = """
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND LOWER(table_name) IN (
                  'oauthclients',
                  'oauthauthorizationcodes',
                  'oauthaccesstokens',
                  'oauthrefreshtokens',
                  'oauthauditlog'
              )
            """;

        var count = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal(5L, count);
    }

    [Fact]
    public async Task Step19_OAuthClientsTable_HasZeroRows()
    {
        await using var ctx = NewContext();
        await ctx.Database.MigrateAsync();

        // Querying via DbSet proves EF mapping is correct
        var count = await ctx.OAuthClients.CountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Step19_AllOAuthDbSets_AreQueryable()
    {
        await using var ctx = NewContext();
        await ctx.Database.MigrateAsync();

        Assert.Equal(0, await ctx.OAuthClients.CountAsync());
        Assert.Equal(0, await ctx.OAuthAuthorizationCodes.CountAsync());
        Assert.Equal(0, await ctx.OAuthAccessTokens.CountAsync());
        Assert.Equal(0, await ctx.OAuthRefreshTokens.CountAsync());
        Assert.Equal(0, await ctx.OAuthAuditLog.CountAsync());
    }
}
