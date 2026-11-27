using Geef.Atelier.Core.Domain.Providers;
using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Geef.Atelier.Tests.Persistence;

/// <summary>
/// The one baseline migration (D-092): it builds the schema the model describes with the rows a fresh installation needs, and the
/// start refuses a database that does not come from it before writing anything. Real Postgres without the pgvector extension.
/// </summary>
[Trait("Category", "Integration")]
public sealed class BaselineMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task The_model_and_the_migrations_describe_the_same_schema()
    {
        await using var db = Context(await CreateDatabaseAsync());

        Assert.False(db.Database.HasPendingModelChanges());
        Assert.EndsWith("_Baseline", db.Database.GetMigrations().First(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_fresh_database_has_the_system_providers_the_site_settings_and_no_vector_extension()
    {
        var connection = await CreateDatabaseAsync();

        await StartAsync(connection);

        await using var db = Context(connection);
        var providers = await db.Database.SqlQueryRaw<string>(
            """SELECT "Name" || ':' || "IsSystem" || ':' || "IsActive" AS "Value" FROM "Providers" ORDER BY "Name" """).ToListAsync();
        Assert.Equal(
            new[] { "azure-openai", "claude-cli", "codex-cli", "deepseek", "gemini-cli", "google-ai-studio", "groq", "ollama-local", "openai-compatible-generic", "openai-direct", "openrouter" }
                .Select(n => n + ":true:true"),
            providers);
        Assert.All(providers, p => Assert.True(SystemProviders.IsSystemProviderName(p.Split(':')[0]), p));
        Assert.Equal(1, await ScalarAsync(db, """SELECT count(*)::int AS "Value" FROM "SiteSettings" WHERE "Id" = '00000000-0000-0000-0000-000000000001' """));
        Assert.Equal(0, await ScalarAsync(db, """SELECT count(*)::int AS "Value" FROM pg_extension WHERE extname = 'vector' """));
    }

    [Fact]
    public async Task Starting_twice_changes_nothing()
    {
        var connection = await CreateDatabaseAsync();
        await StartAsync(connection);
        var before = await SchemaAndHistoryAsync(connection);

        await StartAsync(connection);

        Assert.Equal(before, await SchemaAndHistoryAsync(connection));
    }

    [Fact]
    public async Task A_missing_database_is_created()
    {
        var connection = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = $"missing_{Guid.NewGuid():N}" }.ConnectionString;

        await StartAsync(connection);

        await using var db = Context(connection);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task A_database_of_an_earlier_version_is_refused_and_left_as_it_was()
    {
        var connection = await CreateDatabaseAsync();
        await ExecuteAsync(connection,
            """
            CREATE TABLE "__EFMigrationsHistory" ("MigrationId" varchar(150) PRIMARY KEY, "ProductVersion" varchar(32) NOT NULL);
            INSERT INTO "__EFMigrationsHistory" VALUES ('20260510094017_InitialCreate', '10.0.0'), ('20261130120000_Step53Uploads', '10.0.0');
            CREATE TABLE "Runs" ("Id" uuid PRIMARY KEY, "Kind" integer NOT NULL);
            """);
        var before = await SchemaAndHistoryAsync(connection);

        var error = await Assert.ThrowsAsync<IncompatibleDatabaseException>(() => StartAsync(connection));

        Assert.Contains("created by an earlier version", error.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing was changed", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, await SchemaAndHistoryAsync(connection));
    }

    [Fact]
    public async Task A_database_with_tables_but_without_history_is_refused_and_gets_no_history_table()
    {
        var connection = await CreateDatabaseAsync();
        await ExecuteAsync(connection, """CREATE TABLE "Users" ("UserId" text PRIMARY KEY);""");
        var before = await SchemaAndHistoryAsync(connection);

        var error = await Assert.ThrowsAsync<IncompatibleDatabaseException>(() => StartAsync(connection));

        Assert.Contains("no migration history", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, await SchemaAndHistoryAsync(connection));
    }

    [Fact]
    public async Task A_later_additive_migration_unknown_to_this_version_does_not_stop_the_start()
    {
        var connection = await CreateDatabaseAsync();
        await StartAsync(connection);
        await ExecuteAsync(connection, """INSERT INTO "__EFMigrationsHistory" VALUES ('29990101000000_LaterAddition', '10.0.0');""");

        await StartAsync(connection);
    }

    private async Task<string> CreateDatabaseAsync()
    {
        var name = $"baseline_{Guid.NewGuid():N}";
        await ExecuteAsync(_postgres.GetConnectionString(), $"CREATE DATABASE \"{name}\"");
        return new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = name }.ConnectionString;
    }

    private static async Task StartAsync(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddDbContext<AtelierDbContext>(o => o.UseNpgsql(connectionString));
        await using var provider = services.BuildServiceProvider();
        await provider.MigrateDatabaseOrFailAsync();
    }

    private static AtelierDbContext Context(string connectionString) =>
        new(new DbContextOptionsBuilder<AtelierDbContext>().UseNpgsql(connectionString).Options);

    private static Task<int> ScalarAsync(AtelierDbContext db, string sql) => db.Database.SqlQueryRaw<int>(sql).SingleAsync();

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> SchemaAndHistoryAsync(string connectionString)
    {
        await using var db = Context(connectionString);
        var columns = await db.Database.SqlQueryRaw<string>(
            """SELECT table_name || '.' || column_name AS "Value" FROM information_schema.columns WHERE table_schema = 'public' ORDER BY 1""").ToListAsync();
        var history = columns.Any(c => c.StartsWith("__EFMigrationsHistory.", StringComparison.Ordinal))
            ? await db.Database.SqlQueryRaw<string>("""SELECT "MigrationId" AS "Value" FROM "__EFMigrationsHistory" ORDER BY 1""").ToListAsync()
            : [];
        return string.Join(';', columns) + "|" + string.Join(';', history);
    }
}
