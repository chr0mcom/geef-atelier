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
/// Step51 against real Postgres: a Step50 database with an order gains the two nullable research columns, the order keeps <c>NULL</c>,
/// the statement runs again without change and the way back removes the columns.
/// </summary>
[Trait("Category", "Integration")]
public sealed class Step51ResearchMigrationTests : IAsyncLifetime
{
    private const string Step50 = "20261127120000_Step50HonestWriting";
    private const string StoredJson = """{"schemaVersion":1,"status":"NothingFound"}""";

    private static readonly DateTimeOffset T0 = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .Build();

    private readonly Guid _order = Guid.NewGuid();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public void The_migration_carries_both_attributes()
    {
        var type = typeof(Step51Research);
        Assert.NotNull(type.GetCustomAttributes(typeof(DbContextAttribute), false).SingleOrDefault());
        var migration = Assert.IsType<MigrationAttribute>(type.GetCustomAttributes(typeof(MigrationAttribute), false).Single());
        Assert.Equal("20261128120000_Step51Research", migration.Id);
    }

    [Fact]
    public async Task A_step50_database_gains_the_columns_and_keeps_its_orders_null()
    {
        await MigrateToStep50WithAnOrderAsync();

        await using (var db = NewContext())
            await db.Database.MigrateAsync();

        await using var check = NewContext();
        Assert.Empty(await check.Database.GetPendingMigrationsAsync());
        await AssertColumnsAsync(check);

        var order = await check.Orders.AsNoTracking().SingleAsync(o => o.Id == _order);
        Assert.Null(order.ResearchJson);
        Assert.Null(order.ResearchAt);
        Assert.Equal("Write a note.", order.RequestText);
    }

    [Fact]
    public async Task Running_up_twice_changes_nothing_and_keeps_stored_research()
    {
        await MigrateToStep50WithAnOrderAsync();
        await using (var db = NewContext())
            await db.Database.MigrateAsync();

        await using (var db = NewContext())
        {
            await db.Orders.Where(o => o.Id == _order).ExecuteUpdateAsync(s => s
                .SetProperty(o => o.ResearchJson, StoredJson)
                .SetProperty(o => o.ResearchAt, T0));
            await RunUpAsync(db);
            await RunUpAsync(db);
            await AssertColumnsAsync(db);
        }

        await using var read = NewContext();
        var order = await read.Orders.AsNoTracking().SingleAsync(o => o.Id == _order);
        Assert.Equal(StoredJson, order.ResearchJson);
        Assert.Equal(T0, order.ResearchAt!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Down_removes_the_columns_and_up_restores_them()
    {
        await MigrateToStep50WithAnOrderAsync();
        await using (var db = NewContext())
            await db.Database.MigrateAsync();

        await using var raw = NewContext();
        await RunDownAsync(raw);
        await RunDownAsync(raw);
        Assert.Empty(await ColumnsAsync(raw));
        Assert.Equal(1, await raw.Database.SqlQueryRaw<int>("""SELECT count(*)::int AS "Value" FROM "Orders" """).SingleAsync());

        await RunUpAsync(raw);
        await AssertColumnsAsync(raw);
    }

    private async Task MigrateToStep50WithAnOrderAsync()
    {
        await using (var db = NewContext())
            await db.GetService<IMigrator>().MigrateAsync(Step50);

        await using var seed = NewContext();
        await seed.Database.ExecuteSqlAsync($"""
            INSERT INTO "Orders" ("Id", "CreatedByUser", "RequestText", "Mode", "State", "Entry", "CreatedAt", "UpdatedAt", "IntakeAttempts")
            VALUES ({_order}, 'user-a', 'Write a note.', 'Direct', 'Ready', 'Web', {T0}, {T0}, 0)
            """);
    }

    private static async Task AssertColumnsAsync(AtelierDbContext db)
    {
        var columns = (await ColumnsAsync(db)).ToDictionary(c => c.Name);
        Assert.Equal(2, columns.Count);
        Assert.Equal(("text", "YES"), (columns["ResearchJson"].Type, columns["ResearchJson"].Nullable));
        Assert.Equal(("timestamp with time zone", "YES"), (columns["ResearchAt"].Type, columns["ResearchAt"].Nullable));
    }

    private static Task<List<ColumnRow>> ColumnsAsync(AtelierDbContext db) =>
        db.Database.SqlQueryRaw<ColumnRow>("""
            SELECT column_name AS "Name", data_type AS "Type", is_nullable AS "Nullable"
            FROM information_schema.columns
            WHERE table_name = 'Orders' AND column_name IN ('ResearchJson', 'ResearchAt')
            """).ToListAsync();

    private AtelierDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AtelierDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options);

    private static async Task RunUpAsync(AtelierDbContext db)
    {
        foreach (var sql in new Step51Research().UpOperations.OfType<SqlOperation>().Select(o => o.Sql))
            await db.Database.ExecuteSqlRawAsync(sql.Replace("{", "{{").Replace("}", "}}"));
    }

    private static async Task RunDownAsync(AtelierDbContext db)
    {
        foreach (var sql in new Step51Research().DownOperations.OfType<SqlOperation>().Select(o => o.Sql))
            await db.Database.ExecuteSqlRawAsync(sql.Replace("{", "{{").Replace("}", "}}"));
    }

    private sealed record ColumnRow(string Name, string Type, string Nullable);
}
