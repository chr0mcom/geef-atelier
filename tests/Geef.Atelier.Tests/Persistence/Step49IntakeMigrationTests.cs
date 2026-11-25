using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Profiles;
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
/// Step49 against real Postgres: a Step48 database with an order gains the intake columns, indexes and tables; the
/// statements run again without change; an order's shadow row goes with the order; the way back removes everything and
/// the existing order keeps working.
/// </summary>
[Trait("Category", "Integration")]
public sealed class Step49IntakeMigrationTests : IAsyncLifetime
{
    private const string Step48 = "20261125120000_Step48OrdersAndSources";

    private static readonly DateTimeOffset T0 = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);

    private static readonly string[] OrderColumns =
        ["IntakeOutcome", "IntakeAttempts", "IntakeStartedAt", "IntakeFinishedAt", "QuestionsAskedAt", "AnsweredAt"];

    private static readonly string[] Indexes =
        ["IX_Orders_Intake_Analyzing", "IX_Orders_Intake_NeedsInput", "IX_OrderIntakeShadows_Pending", "PK_UserProfiles", "PK_OrderIntakeShadows"];

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .Build();

    private readonly Guid _existingOrder = Guid.NewGuid();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task AStep48DatabaseGainsTheIntakeSchemaAndKeepsItsOrders()
    {
        await MigrateToStep48WithAnOrderAsync();

        await using (var db = NewContext())
            await db.Database.MigrateAsync();

        await using var check = NewContext();
        Assert.Empty(await check.Database.GetPendingMigrationsAsync());
        await AssertSchemaAsync(check);

        var order = await check.Orders.AsNoTracking().SingleAsync(o => o.Id == _existingOrder);
        Assert.Null(order.IntakeOutcome);
        Assert.Equal(0, order.IntakeAttempts);
        Assert.Null(order.IntakeStartedAt);
        Assert.Null(order.QuestionsAskedAt);
        Assert.Equal(OrderState.Ready, order.State);
    }

    [Fact]
    public async Task RunningTheStatementsAgainChangesNothing()
    {
        await MigrateToStep48WithAnOrderAsync();
        await using (var db = NewContext())
            await db.Database.MigrateAsync();

        await using (var db = NewContext())
        {
            db.UserProfiles.Add(new UserProfileEntity
            {
                Username = "u1", DisplayName = "Anna Beispiel", Formality = Formality.Formal, CreatedAt = T0, UpdatedAt = T0,
            });
            await db.SaveChangesAsync();

            await RunUpAsync(db);
            await RunUpAsync(db);

            await AssertSchemaAsync(db);
            var profile = await db.UserProfiles.AsNoTracking().SingleAsync();
            Assert.Equal(("Anna Beispiel", Formality.Formal), (profile.DisplayName, profile.Formality));
            Assert.Equal(1, await db.Orders.CountAsync());
        }
    }

    [Fact]
    public async Task DefaultsFitTheOldModelAndTheShadowRowGoesWithItsOrder()
    {
        await using (var db = NewContext())
            await db.Database.MigrateAsync();

        var orderId = Guid.NewGuid();
        await using (var db = NewContext())
        {
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "Orders" ("Id", "CreatedByUser", "RequestText", "Mode", "State", "Entry", "CreatedAt", "UpdatedAt")
                VALUES ({orderId}, 'u1', 'Write.', 'Direct', 'Ready', 'Web', {T0}, {T0})
                """);
            await db.Database.ExecuteSqlAsync($"""INSERT INTO "UserProfiles" ("Username", "CreatedAt", "UpdatedAt") VALUES ('u2', {T0}, {T0})""");

            db.OrderIntakeShadows.Add(new OrderIntakeShadowEntity { OrderId = orderId, Status = IntakeShadowStatuses.Pending, CreatedAt = T0 });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var order = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
            Assert.Equal(0, order.IntakeAttempts);
            Assert.Null(order.IntakeOutcome);
            Assert.Equal(Formality.Unset, (await db.UserProfiles.AsNoTracking().SingleAsync(p => p.Username == "u2")).Formality);
            Assert.Equal(0, (await db.OrderIntakeShadows.AsNoTracking().SingleAsync()).Attempts);

            await db.Orders.Where(o => o.Id == orderId).ExecuteDeleteAsync();
            Assert.Equal(0, await db.OrderIntakeShadows.CountAsync());
        }
    }

    [Fact]
    public async Task DownRemovesTheSchemaAndUpRestoresIt()
    {
        await MigrateToStep48WithAnOrderAsync();
        await using (var db = NewContext())
            await db.Database.MigrateAsync();

        await using (var db = NewContext())
        {
            await RunDownAsync(db);
            await RunDownAsync(db);

            Assert.Empty(await NamesAsync(db, """
                SELECT table_name AS "Value" FROM information_schema.tables WHERE table_name IN ('UserProfiles', 'OrderIntakeShadows')
                """));
            Assert.Empty(await NamesAsync(db, """
                SELECT column_name AS "Value" FROM information_schema.columns
                WHERE table_name = 'Orders' AND column_name IN ('IntakeOutcome', 'IntakeAttempts', 'IntakeStartedAt', 'IntakeFinishedAt', 'QuestionsAskedAt', 'AnsweredAt')
                """));
            Assert.Empty(await NamesAsync(db, """
                SELECT indexname AS "Value" FROM pg_indexes WHERE indexname IN ('IX_Orders_Intake_Analyzing', 'IX_Orders_Intake_NeedsInput')
                """));
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>("""SELECT count(*)::int AS "Value" FROM "Orders" """).SingleAsync());

            await RunUpAsync(db);
            await AssertSchemaAsync(db);
        }
    }

    private async Task MigrateToStep48WithAnOrderAsync()
    {
        await using (var db = NewContext())
            await db.GetService<IMigrator>().MigrateAsync(Step48);

        await using var seed = NewContext();
        await seed.Database.ExecuteSqlAsync($"""
            INSERT INTO "Orders" ("Id", "CreatedByUser", "RequestText", "Mode", "State", "Entry", "CreatedAt", "UpdatedAt")
            VALUES ({_existingOrder}, 'u1', 'Write a short note.', 'Careful', 'Ready', 'Legacy', {T0}, {T0})
            """);
    }

    private static async Task AssertSchemaAsync(AtelierDbContext db)
    {
        var columns = await db.Database.SqlQueryRaw<ColumnRow>("""
            SELECT column_name AS "Name", data_type AS "Type", is_nullable AS "Nullable", column_default AS "Default",
                   character_maximum_length AS "Length"
            FROM information_schema.columns WHERE table_name = 'Orders'
            """).ToDictionaryAsync(c => c.Name);
        foreach (var name in OrderColumns)
            Assert.True(columns.ContainsKey(name), name);
        Assert.Equal(("character varying", 20, "YES"), (columns["IntakeOutcome"].Type, columns["IntakeOutcome"].Length, columns["IntakeOutcome"].Nullable));
        Assert.Equal(("integer", "NO", "0"), (columns["IntakeAttempts"].Type, columns["IntakeAttempts"].Nullable, columns["IntakeAttempts"].Default));
        Assert.Equal("timestamp with time zone", columns["QuestionsAskedAt"].Type);

        Assert.Equal(["OrderIntakeShadows", "UserProfiles"], (await NamesAsync(db, """
            SELECT table_name AS "Value" FROM information_schema.tables WHERE table_name IN ('UserProfiles', 'OrderIntakeShadows')
            """)).Order(StringComparer.Ordinal));

        foreach (var index in Indexes)
            Assert.Equal(1, await db.Database.SqlQuery<int>($"""SELECT count(*)::int AS "Value" FROM pg_indexes WHERE indexname = {index}""").SingleAsync());
        var analyzing = await db.Database.SqlQueryRaw<string>("""SELECT indexdef AS "Value" FROM pg_indexes WHERE indexname = 'IX_Orders_Intake_Analyzing'""").SingleAsync();
        Assert.Contains("'Analyzing'", analyzing, StringComparison.Ordinal);
        var waiting = await db.Database.SqlQueryRaw<string>("""SELECT indexdef AS "Value" FROM pg_indexes WHERE indexname = 'IX_Orders_Intake_NeedsInput'""").SingleAsync();
        Assert.Contains("\"QuestionsAskedAt\"", waiting, StringComparison.Ordinal);
        Assert.Contains("'NeedsInput'", waiting, StringComparison.Ordinal);

        var cascade = await db.Database.SqlQueryRaw<string>("""
            SELECT confdeltype::text AS "Value" FROM pg_constraint WHERE conname = 'FK_OrderIntakeShadows_Orders_OrderId'
            """).ToListAsync();
        Assert.Equal(["c"], cascade);
    }

    private static Task<List<string>> NamesAsync(AtelierDbContext db, string sql) =>
        db.Database.SqlQueryRaw<string>(sql).ToListAsync();

    private AtelierDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AtelierDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options);

    private static async Task RunUpAsync(AtelierDbContext db)
    {
        foreach (var sql in new Step49Intake().UpOperations.OfType<SqlOperation>().Select(o => o.Sql))
            await db.Database.ExecuteSqlRawAsync(sql.Replace("{", "{{").Replace("}", "}}"));
    }

    private static async Task RunDownAsync(AtelierDbContext db)
    {
        foreach (var sql in new Step49Intake().DownOperations.OfType<SqlOperation>().Select(o => o.Sql))
            await db.Database.ExecuteSqlRawAsync(sql.Replace("{", "{{").Replace("}", "}}"));
    }

    private sealed record ColumnRow(string Name, string Type, string Nullable, string? Default, int? Length);
}
