using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Geef.Atelier.Tests.Persistence;

/// <summary>
/// Step48 against real Postgres: a Step47 database with run chains (composition → task, resume, resume of the
/// composition, learning, orphaned parent, composition without original task) becomes one order per chain; the
/// statements run again without change; a fresh database, the way back and the foreign keys behave as specified.
/// </summary>
[Trait("Category", "Integration")]
public sealed class Step48OrdersAndSourcesMigrationTests : IAsyncLifetime
{
    private const string Step47 = "20261124120000_Step47EngineHonesty";

    private static readonly DateTimeOffset T0 = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .Build();

    private readonly Guid _compositionA = Guid.NewGuid();
    private readonly Guid _taskA = Guid.NewGuid();
    private readonly Guid _resumeA = Guid.NewGuid();
    private readonly Guid _single = Guid.NewGuid();
    private readonly Guid _compositionC = Guid.NewGuid();
    private readonly Guid _compositionCResumed = Guid.NewGuid();
    private readonly Guid _taskC = Guid.NewGuid();
    private readonly Guid _learning = Guid.NewGuid();
    private readonly Guid _learningChild = Guid.NewGuid();
    private readonly Guid _orphan = Guid.NewGuid();
    private readonly Guid _compositionF = Guid.NewGuid();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task TurnsEveryRunChainOfAStep47DatabaseIntoOneOrder()
    {
        await MigrateToStep47AndSeedChainsAsync();

        await using (var db = NewContext())
            await db.Database.MigrateAsync();

        await using var check = NewContext();
        Assert.Empty(await check.Database.GetPendingMigrationsAsync());
        await AssertChainsMigratedAsync(check);
    }

    [Fact]
    public async Task RunningTheStatementsAgainChangesNothingAndNewRunsNeverBecomeRoots()
    {
        await MigrateToStep47AndSeedChainsAsync();
        await using (var db = NewContext())
            await db.Database.MigrateAsync();

        var newOrderId = Guid.NewGuid();
        await using (var db = NewContext())
        {
            db.Orders.Add(NewOrder(newOrderId, "new"));
            db.Runs.Add(new RunEntity
            {
                Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow, Status = RunStatus.Pending, BriefingText = "new run",
                ConfigJson = "{}", OrderId = newOrderId, OrderVersion = 1,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var before = await SnapshotAsync(db);
            await RunUpAsync(db);
            await RunUpAsync(db);
            var after = await SnapshotAsync(db);
            Assert.Equal(before, after);
            Assert.Equal(7, await db.Orders.CountAsync());
            await AssertChainsMigratedAsync(db);
        }
    }

    [Fact]
    public async Task RunsTwiceOnAFreshDatabaseAndCreatesTheSchema()
    {
        await using (var db = NewContext())
            await db.Database.MigrateAsync();

        await using (var db = NewContext())
        {
            await RunUpAsync(db);
            await RunUpAsync(db);
            await AssertSchemaAsync(db);
            Assert.Equal(0, await db.Orders.CountAsync());
        }
    }

    [Fact]
    public async Task DownRemovesTheSchemaAndUpRestoresIt()
    {
        await MigrateToStep47AndSeedChainsAsync();
        await using (var db = NewContext())
            await db.Database.MigrateAsync();

        await using (var db = NewContext())
        {
            await RunDownAsync(db);
            await RunDownAsync(db);

            var tables = await db.Database.SqlQueryRaw<string>("""
                SELECT table_name AS "Value" FROM information_schema.tables
                WHERE table_name IN ('Orders', 'Sources', 'OrderSources', 'RunSourceUsages')
                """).ToListAsync();
            Assert.Empty(tables);
            var columns = await db.Database.SqlQueryRaw<string>("""
                SELECT column_name AS "Value" FROM information_schema.columns
                WHERE table_name = 'Runs' AND column_name IN ('OrderId', 'OrderVersion')
                """).ToListAsync();
            Assert.Empty(columns);
            Assert.Equal(11, await db.Database.SqlQueryRaw<int>("""SELECT count(*)::int AS "Value" FROM "Runs" """).SingleAsync());

            await RunUpAsync(db);
            await AssertSchemaAsync(db);
            await AssertChainsMigratedAsync(db);
        }
    }

    [Fact]
    public async Task ForeignKeysCascadeRestrictAndSetNullAndAVersionStartsOnlyOnce()
    {
        await using (var db = NewContext())
            await db.Database.MigrateAsync();

        var orderId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var looseSourceId = Guid.NewGuid();
        await using (var db = NewContext())
        {
            db.Orders.Add(NewOrder(orderId, "fk"));
            db.Sources.Add(NewSource(sourceId));
            db.Sources.Add(NewSource(looseSourceId));
            db.OrderSources.Add(new OrderSourceEntity { OrderId = orderId, SourceId = sourceId, Role = SourceRole.Facts, Position = 0, AddedAt = T0 });
            db.Runs.Add(new RunEntity
            {
                Id = runId, CreatedAt = T0, Status = RunStatus.Completed, BriefingText = "b", ConfigJson = "{}",
                OrderId = orderId, OrderVersion = 1,
            });
            db.RunSourceUsages.Add(NewUsage(runId, sourceId));
            db.RunSourceUsages.Add(NewUsage(runId, looseSourceId));
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            db.Runs.Add(new RunEntity
            {
                Id = Guid.NewGuid(), CreatedAt = T0, Status = RunStatus.Pending, BriefingText = "b", ConfigJson = "{}",
                OrderId = orderId, OrderVersion = 1,
            });
            var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(duplicate.InnerException).SqlState);
        }

        await using (var db = NewContext())
        {
            db.Runs.Add(new RunEntity
            {
                Id = Guid.NewGuid(), CreatedAt = T0, Status = RunStatus.Pending, BriefingText = "b", ConfigJson = "{}",
                Kind = RunKind.CrewComposition, OrderId = orderId, OrderVersion = 1,
            });
            await db.SaveChangesAsync();

            var restricted = await Assert.ThrowsAsync<PostgresException>(() =>
                db.Sources.Where(s => s.Id == sourceId).ExecuteDeleteAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, restricted.SqlState);

            await db.Sources.Where(s => s.Id == looseSourceId).ExecuteDeleteAsync();
            var usage = await db.RunSourceUsages.AsNoTracking().SingleAsync(u => u.FileName == looseSourceId.ToString());
            Assert.Null(usage.SourceId);

            await db.Orders.Where(o => o.Id == orderId).ExecuteDeleteAsync();
            Assert.Equal(0, await db.Runs.CountAsync(r => r.OrderId == orderId));
            Assert.Equal(0, await db.OrderSources.CountAsync(l => l.OrderId == orderId));
            Assert.Equal(0, await db.RunSourceUsages.CountAsync(u => u.RunId == runId));
            Assert.True(await db.Sources.AnyAsync(s => s.Id == sourceId));
        }
    }

    private async Task MigrateToStep47AndSeedChainsAsync()
    {
        await using (var db = NewContext())
            await db.GetService<IMigrator>().MigrateAsync(Step47);

        await using var seed = NewContext();
        await InsertRunAsync(seed, _compositionA, 0, kind: 2, status: "Completed", completedMinutes: 1,
            briefing: "Compose a crew for the following task:\n\nWrite the spring newsletter.",
            config: """{"original_task":"Write the spring newsletter.","chain_to_task_run":true}""");
        await InsertRunAsync(seed, _taskA, 2, kind: 0, status: "Failed", completedMinutes: 3, parentComposition: _compositionA, briefing: "Write the spring newsletter.");
        await InsertRunAsync(seed, _resumeA, 4, kind: 0, status: "Completed", completedMinutes: 9, parentRun: _taskA, briefing: "Write the spring newsletter.");
        await InsertRunAsync(seed, _single, 10, kind: 0, status: "Completed", completedMinutes: 11, briefing: "A short thank-you note.");
        await InsertRunAsync(seed, _compositionC, 20, kind: 2, status: "Failed", completedMinutes: 21,
            briefing: "Compose a crew for the following task:\n\nDraft a tender reply.",
            config: """{"original_task":"Draft a tender reply."}""");
        await InsertRunAsync(seed, _compositionCResumed, 22, kind: 2, status: "Completed", completedMinutes: 23, parentRun: _compositionC,
            briefing: "Compose a crew for the following task:\n\nDraft a tender reply.",
            config: """{"original_task":"Draft a tender reply."}""");
        await InsertRunAsync(seed, _taskC, 24, kind: 0, status: "Completed", completedMinutes: 30, parentComposition: _compositionCResumed, briefing: "Draft a tender reply.");
        await InsertRunAsync(seed, _learning, 31, kind: 1, status: "Completed", completedMinutes: 32, briefing: "learning");
        await InsertRunAsync(seed, _learningChild, 33, kind: 0, status: "Completed", completedMinutes: 34, parentRun: _learning, briefing: "After a learning run.");
        await InsertRunAsync(seed, _orphan, 40, kind: 0, status: "Completed", completedMinutes: null, parentRun: Guid.NewGuid(), briefing: "Parent was deleted.");
        await InsertRunAsync(seed, _compositionF, 50, kind: 2, status: "Completed", completedMinutes: 51,
            briefing: "Compose a crew for the following task:\n\nPlan the team offsite.", config: "{}");
    }

    private async Task AssertChainsMigratedAsync(AtelierDbContext db)
    {
        Assert.Equal(0, await db.Runs.CountAsync(r => r.Kind != RunKind.Learning && r.OrderId == null));
        Assert.Null((await db.Runs.AsNoTracking().SingleAsync(r => r.Id == _learning)).OrderId);

        var orders = await db.Orders.AsNoTracking().ToDictionaryAsync(o => o.Id);
        Assert.True(orders.ContainsKey(_compositionA));
        Assert.True(orders.ContainsKey(_single));
        Assert.True(orders.ContainsKey(_compositionC));
        Assert.True(orders.ContainsKey(_learningChild));
        Assert.True(orders.ContainsKey(_orphan));
        Assert.True(orders.ContainsKey(_compositionF));

        var a = orders[_compositionA];
        Assert.Equal("Write the spring newsletter.", a.RequestText);
        Assert.Equal(OrderMode.Careful, a.Mode);
        Assert.Equal(OrderState.Ready, a.State);
        Assert.Equal(OrderEntry.Legacy, a.Entry);
        Assert.Equal("u1", a.CreatedByUser);
        Assert.Equal(T0, a.CreatedAt);
        Assert.Equal(T0.AddMinutes(9), a.UpdatedAt);

        Assert.Equal("Draft a tender reply.", orders[_compositionC].RequestText);
        Assert.Equal("Plan the team offsite.", orders[_compositionF].RequestText);
        Assert.Equal("A short thank-you note.", orders[_single].RequestText);
        Assert.Equal(T0.AddMinutes(40), orders[_orphan].UpdatedAt);

        await AssertLinkAsync(db, _compositionA, _compositionA, 1);
        await AssertLinkAsync(db, _taskA, _compositionA, 1);
        await AssertLinkAsync(db, _resumeA, _compositionA, 2);
        await AssertLinkAsync(db, _single, _single, 1);
        await AssertLinkAsync(db, _compositionC, _compositionC, 1);
        await AssertLinkAsync(db, _compositionCResumed, _compositionC, 1);
        await AssertLinkAsync(db, _taskC, _compositionC, 1);
        await AssertLinkAsync(db, _learningChild, _learningChild, 1);
        await AssertLinkAsync(db, _orphan, _orphan, 1);
        await AssertLinkAsync(db, _compositionF, _compositionF, 1);
    }

    private static async Task AssertLinkAsync(AtelierDbContext db, Guid runId, Guid orderId, int version)
    {
        var run = await db.Runs.AsNoTracking().SingleAsync(r => r.Id == runId);
        Assert.Equal(orderId, run.OrderId);
        Assert.Equal(version, run.OrderVersion);
    }

    private static async Task<string> SnapshotAsync(AtelierDbContext db)
    {
        var runs = await db.Database.SqlQueryRaw<string>("""
            SELECT string_agg("Id"::text || ':' || COALESCE("OrderId"::text, '-') || ':' || COALESCE("OrderVersion"::text, '-'), ',' ORDER BY "Id") AS "Value" FROM "Runs"
            """).SingleAsync();
        var orders = await db.Database.SqlQueryRaw<string>("""
            SELECT string_agg("Id"::text || ':' || "RequestText" || ':' || "UpdatedAt"::text, ',' ORDER BY "Id") AS "Value" FROM "Orders"
            """).SingleAsync();
        return runs + "|" + orders;
    }

    private static async Task AssertSchemaAsync(AtelierDbContext db)
    {
        var tables = await db.Database.SqlQueryRaw<string>("""
            SELECT table_name AS "Value" FROM information_schema.tables
            WHERE table_name IN ('Orders', 'Sources', 'OrderSources', 'RunSourceUsages')
            """).ToListAsync();
        Assert.Equal(["OrderSources", "Orders", "RunSourceUsages", "Sources"], tables.Order(StringComparer.Ordinal).ToList());

        foreach (var fk in new[]
                 {
                     "FK_OrderSources_Orders_OrderId", "FK_OrderSources_Sources_SourceId", "FK_RunSourceUsages_Runs_RunId",
                     "FK_RunSourceUsages_Sources_SourceId", "FK_Runs_Orders_OrderId",
                 })
            Assert.Equal(1, await db.Database.SqlQuery<int>($"""SELECT count(*)::int AS "Value" FROM pg_constraint WHERE conname = {fk}""").SingleAsync());

        foreach (var index in new[]
                 {
                     "IX_Orders_CreatedByUser_CreatedAt", "IX_Orders_State", "IX_Sources_OwnerUsername_CreatedAt",
                     "UX_Sources_OwnerUsername_Sha256", "IX_Sources_ExpiresAt", "IX_OrderSources_SourceId",
                     "IX_RunSourceUsages_RunId", "IX_Runs_OrderId_OrderVersion", "UX_Runs_OrderId_OrderVersion_Standard",
                 })
            Assert.Equal(1, await db.Database.SqlQuery<int>($"""SELECT count(*)::int AS "Value" FROM pg_indexes WHERE indexname = {index}""").SingleAsync());

        var unique = await db.Database.SqlQueryRaw<string>("""
            SELECT indexdef AS "Value" FROM pg_indexes WHERE indexname = 'UX_Runs_OrderId_OrderVersion_Standard'
            """).SingleAsync();
        Assert.Contains("UNIQUE", unique, StringComparison.Ordinal);
        Assert.Contains("\"Kind\" = 0", unique, StringComparison.Ordinal);

        var sourceHash = await db.Database.SqlQueryRaw<string>("""
            SELECT indexdef AS "Value" FROM pg_indexes WHERE indexname = 'UX_Sources_OwnerUsername_Sha256'
            """).SingleAsync();
        Assert.Contains("UNIQUE", sourceHash, StringComparison.Ordinal);
        Assert.Equal(0, await db.Database.SqlQuery<int>($"""SELECT count(*)::int AS "Value" FROM pg_indexes WHERE indexname = 'IX_Sources_OwnerUsername_Sha256'""").SingleAsync());
    }

    private static Task InsertRunAsync(
        AtelierDbContext db, Guid id, int minutes, int kind, string status, int? completedMinutes, string briefing,
        string config = "{}", Guid? parentRun = null, Guid? parentComposition = null)
    {
        var createdAt = T0.AddMinutes(minutes);
        DateTimeOffset? completedAt = completedMinutes is { } c ? T0.AddMinutes(c) : null;
        return db.Database.ExecuteSqlAsync($"""
            INSERT INTO "Runs" ("Id", "CreatedAt", "Status", "BriefingText", "ConfigJson", "CreatedByUser", "TokensTotal", "CostTotal",
                                "CancellationRequested", "AdvisorRetryAttempted", "Kind", "ParentRunId", "ParentCompositionRunId", "CompletedAt")
            VALUES ({id}, {createdAt}, {status}, {briefing}, {config}::jsonb, 'u1', 0, 0, false, false, {kind}, {parentRun}, {parentComposition}, {completedAt})
            """);
    }

    private static OrderEntity NewOrder(Guid id, string text) => new()
    {
        Id = id, CreatedByUser = "u1", RequestText = text, Mode = OrderMode.Direct, State = OrderState.Ready,
        Entry = OrderEntry.Web, CreatedAt = T0, UpdatedAt = T0,
    };

    private static SourceEntity NewSource(Guid id) => new()
    {
        Id = id, OwnerUsername = "u1", FileName = "notes.md", ContentType = "text/markdown", Kind = SourceKind.Markdown,
        SizeBytes = 10, Sha256 = Convert.ToHexStringLower(id.ToByteArray()).PadRight(64, 'a'), CharCount = 10, WordCount = 2, Quality = SourceQuality.Good,
        Warnings = [], Origin = SourceOrigin.WebUpload, CreatedAt = T0,
    };

    private static RunSourceUsageEntity NewUsage(Guid runId, Guid sourceId) => new()
    {
        Id = Guid.NewGuid(), RunId = runId, SourceId = sourceId, FileName = sourceId.ToString(), Mode = SourceUsageMode.FullText,
        Target = "writer", CharsIncluded = 10, Position = 0, CreatedAt = T0,
    };

    private AtelierDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AtelierDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options);

    private static async Task RunUpAsync(AtelierDbContext db)
    {
        foreach (var sql in new Step48OrdersAndSources().UpOperations.OfType<SqlOperation>().Select(o => o.Sql))
            await db.Database.ExecuteSqlRawAsync(sql.Replace("{", "{{").Replace("}", "}}"));
    }

    private static async Task RunDownAsync(AtelierDbContext db)
    {
        foreach (var sql in new Step48OrdersAndSources().DownOperations.OfType<SqlOperation>().Select(o => o.Sql))
            await db.Database.ExecuteSqlRawAsync(sql.Replace("{", "{{").Replace("}", "}}"));
    }
}
