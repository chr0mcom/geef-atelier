using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Testcontainers.PostgreSql;

namespace Geef.Atelier.Tests.Persistence;

/// <summary>
/// Step52 against real Postgres: a Step51 database with an order and a run gains the nullable column <c>Runs.RevisionHint</c>, the run
/// keeps <c>NULL</c>, the statement runs again without change and the way back removes the column.
/// </summary>
[Trait("Category", "Integration")]
public sealed class Step52ReviseMigrationTests : IAsyncLifetime
{
    private const string Step51 = "20261128120000_Step51Research";
    private const string EmptyJson = "{}";
    private const string StoredHint = "The meeting is on 5 May, not 12 May.";

    private static readonly DateTimeOffset T0 = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .Build();

    private readonly Guid _order = Guid.NewGuid();
    private readonly Guid _run = Guid.NewGuid();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public void The_migration_carries_both_attributes()
    {
        var type = typeof(Step52Revise);
        Assert.NotNull(type.GetCustomAttributes(typeof(DbContextAttribute), false).SingleOrDefault());
        var migration = Assert.IsType<MigrationAttribute>(type.GetCustomAttributes(typeof(MigrationAttribute), false).Single());
        Assert.Equal("20261129120000_Step52Revise", migration.Id);
    }

    [Fact]
    public async Task A_step51_database_gains_the_column_and_keeps_its_runs_null()
    {
        await MigrateToStep51WithARunAsync();

        await using (var db = NewContext())
            await db.Database.MigrateAsync();

        await using var check = NewContext();
        Assert.Empty(await check.Database.GetPendingMigrationsAsync());
        await AssertColumnAsync(check);

        var run = await check.Runs.AsNoTracking().SingleAsync(r => r.Id == _run);
        Assert.Null(run.RevisionHint);
        Assert.Equal("Done.", run.FinalText);
        Assert.Equal(1, run.OrderVersion);
    }

    [Fact]
    public async Task Running_up_twice_changes_nothing_and_keeps_a_stored_change()
    {
        await MigrateToStep51WithARunAsync();
        await using (var db = NewContext())
            await db.Database.MigrateAsync();

        await using (var db = NewContext())
        {
            await db.Runs.Where(r => r.Id == _run).ExecuteUpdateAsync(s => s.SetProperty(r => r.RevisionHint, StoredHint));
            await RunUpAsync(db);
            await RunUpAsync(db);
            await AssertColumnAsync(db);
        }

        await using var read = NewContext();
        Assert.Equal(StoredHint, (await read.Runs.AsNoTracking().SingleAsync(r => r.Id == _run)).RevisionHint);
    }

    [Fact]
    public async Task Down_removes_the_column_and_up_restores_it()
    {
        await MigrateToStep51WithARunAsync();
        await using (var db = NewContext())
            await db.Database.MigrateAsync();

        await using var raw = NewContext();
        await RunDownAsync(raw);
        await RunDownAsync(raw);
        Assert.Empty(await ColumnsAsync(raw));
        Assert.Equal(1, await raw.Database.SqlQueryRaw<int>("""SELECT count(*)::int AS "Value" FROM "Runs" """).SingleAsync());

        await RunUpAsync(raw);
        await AssertColumnAsync(raw);
    }

    private async Task MigrateToStep51WithARunAsync()
    {
        await using (var db = NewContext())
            await db.GetService<IMigrator>().MigrateAsync(Step51);

        await using var seed = NewContext();
        await seed.Database.ExecuteSqlAsync($"""
            INSERT INTO "Orders" ("Id", "CreatedByUser", "RequestText", "Mode", "State", "Entry", "CreatedAt", "UpdatedAt", "IntakeAttempts")
            VALUES ({_order}, 'user-a', 'Write a note.', 'Direct', 'Ready', 'Web', {T0}, {T0}, 0)
            """);
        await seed.Database.ExecuteSqlAsync($"""
            INSERT INTO "Runs" ("Id", "CreatedAt", "Status", "BriefingText", "ConfigJson", "TokensTotal", "CostTotal", "Kind", "CancellationRequested",
                                "AdvisorRetryAttempted", "FinalText", "OrderId", "OrderVersion")
            VALUES ({_run}, {T0}, 'Completed', 'Write a note.', {EmptyJson}::jsonb, 0, 0, 0, false, false, 'Done.', {_order}, 1)
            """);
    }

    private static async Task AssertColumnAsync(AtelierDbContext db)
    {
        var column = Assert.Single(await ColumnsAsync(db));
        Assert.Equal(("text", "YES"), (column.Type, column.Nullable));
    }

    private static Task<List<ColumnRow>> ColumnsAsync(AtelierDbContext db) =>
        db.Database.SqlQueryRaw<ColumnRow>("""
            SELECT column_name AS "Name", data_type AS "Type", is_nullable AS "Nullable"
            FROM information_schema.columns
            WHERE table_name = 'Runs' AND column_name = 'RevisionHint'
            """).ToListAsync();

    private AtelierDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AtelierDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options);

    private static async Task RunUpAsync(AtelierDbContext db)
    {
        foreach (var sql in new Step52Revise().UpOperations.OfType<SqlOperation>().Select(o => o.Sql))
            await db.Database.ExecuteSqlRawAsync(sql.Replace("{", "{{").Replace("}", "}}"));
    }

    private static async Task RunDownAsync(AtelierDbContext db)
    {
        foreach (var sql in new Step52Revise().DownOperations.OfType<SqlOperation>().Select(o => o.Sql))
            await db.Database.ExecuteSqlRawAsync(sql.Replace("{", "{{").Replace("}", "}}"));
    }

    private sealed record ColumnRow(string Name, string Type, string Nullable);
}
