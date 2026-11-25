using Geef.Atelier.Core.Domain;
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
/// Step50 against real Postgres: a Step49 database with a run and a finding gains the four nullable columns, the existing rows
/// keep <c>NULL</c>, the statements run again without change and the way back removes the columns.
/// </summary>
[Trait("Category", "Integration")]
public sealed class Step50HonestWritingMigrationTests : IAsyncLifetime
{
    private const string Step49 = "20261126120000_Step49Intake";
    private const string EmptyJson = "{}";

    private static readonly DateTimeOffset T0 = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .Build();

    private readonly Guid _run = Guid.NewGuid();
    private readonly Guid _iteration = Guid.NewGuid();
    private readonly Guid _finding = Guid.NewGuid();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public void The_migration_carries_both_attributes()
    {
        var type = typeof(Step50HonestWriting);
        Assert.NotNull(type.GetCustomAttributes(typeof(DbContextAttribute), false).SingleOrDefault());
        var migration = Assert.IsType<MigrationAttribute>(type.GetCustomAttributes(typeof(MigrationAttribute), false).Single());
        Assert.Equal("20261127120000_Step50HonestWriting", migration.Id);
    }

    [Fact]
    public async Task A_step49_database_gains_the_columns_and_keeps_its_rows_null()
    {
        await MigrateToStep49WithARunAsync();

        await using (var db = NewContext())
            await db.Database.MigrateAsync();

        await using var check = NewContext();
        Assert.Empty(await check.Database.GetPendingMigrationsAsync());
        await AssertColumnsAsync(check);

        var run = await check.Runs.AsNoTracking().SingleAsync(r => r.Id == _run);
        Assert.Null(run.FactCheckMode);
        var finding = await check.Findings.AsNoTracking().SingleAsync(f => f.Id == _finding);
        Assert.Equal((null, null, null), (finding.Category, finding.Code, finding.Quote));
        Assert.Equal("The text has 80 words.", finding.Message);
    }

    [Fact]
    public async Task Running_up_twice_changes_nothing_and_new_values_are_stored()
    {
        await MigrateToStep49WithARunAsync();
        await using (var db = NewContext())
            await db.Database.MigrateAsync();

        await using (var db = NewContext())
        {
            await db.Runs.Where(r => r.Id == _run).ExecuteUpdateAsync(s => s.SetProperty(r => r.FactCheckMode, "Report"));
            await RunUpAsync(db);
            await RunUpAsync(db);
            await AssertColumnsAsync(db);
        }

        await using (var db = NewContext())
        {
            Assert.Equal("Report", (await db.Runs.AsNoTracking().SingleAsync(r => r.Id == _run)).FactCheckMode);
            db.Findings.Add(new FindingEntity
            {
                Id = Guid.NewGuid(), IterationId = _iteration, ReviewerName = "source-fact-check", Severity = FindingSeverity.Minor,
                Message = "Not backed.", CreatedAt = T0, Category = "fact-check", Code = "fact.unsupported", Quote = "Fotos liegen bei.",
            });
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        var stored = await read.Findings.AsNoTracking().SingleAsync(f => f.Category == "fact-check");
        Assert.Equal(("fact.unsupported", "Fotos liegen bei."), (stored.Code, stored.Quote));
    }

    [Fact]
    public async Task Down_removes_the_columns_and_up_restores_them()
    {
        await MigrateToStep49WithARunAsync();
        await using (var db = NewContext())
            await db.Database.MigrateAsync();

        await using var raw = NewContext();
        await RunDownAsync(raw);
        await RunDownAsync(raw);
        Assert.Empty(await ColumnsAsync(raw));
        Assert.Equal(1, await raw.Database.SqlQueryRaw<int>("""SELECT count(*)::int AS "Value" FROM "Findings" """).SingleAsync());

        await RunUpAsync(raw);
        await AssertColumnsAsync(raw);
    }

    private async Task MigrateToStep49WithARunAsync()
    {
        await using (var db = NewContext())
            await db.GetService<IMigrator>().MigrateAsync(Step49);

        await using var seed = NewContext();
        await seed.Database.ExecuteSqlAsync($"""
            INSERT INTO "Runs" ("Id", "Status", "BriefingText", "ConfigJson", "CreatedAt", "TokensTotal", "CostTotal", "CancellationRequested", "AdvisorRetryAttempted", "Kind")
            VALUES ({_run}, 'Completed', 'Write a note.', {EmptyJson}::jsonb, {T0}, 0, 0, false, false, 0)
            """);
        await seed.Database.ExecuteSqlAsync($"""
            INSERT INTO "Iterations" ("Id", "RunId", "IterationNumber", "ArtifactText", "CreatedAt")
            VALUES ({_iteration}, {_run}, 1, 'Text.', {T0})
            """);
        await seed.Database.ExecuteSqlAsync($"""
            INSERT INTO "Findings" ("Id", "IterationId", "ReviewerName", "Severity", "Message", "CreatedAt")
            VALUES ({_finding}, {_iteration}, 'deterministic-checks', 'Major', 'The text has 80 words.', {T0})
            """);
    }

    private static async Task AssertColumnsAsync(AtelierDbContext db)
    {
        var columns = (await ColumnsAsync(db)).ToDictionary(c => (c.Table, c.Name));
        Assert.Equal(4, columns.Count);
        Assert.Equal(("character varying", 60, "YES"), Shape(columns[("Findings", "Category")]));
        Assert.Equal(("character varying", 60, "YES"), Shape(columns[("Findings", "Code")]));
        Assert.Equal(("text", (int?)null, "YES"), Shape(columns[("Findings", "Quote")]));
        Assert.Equal(("character varying", 20, "YES"), Shape(columns[("Runs", "FactCheckMode")]));
    }

    private static (string, int?, string) Shape(ColumnRow column) => (column.Type, column.Length, column.Nullable);

    private static Task<List<ColumnRow>> ColumnsAsync(AtelierDbContext db) =>
        db.Database.SqlQueryRaw<ColumnRow>("""
            SELECT table_name AS "Table", column_name AS "Name", data_type AS "Type", is_nullable AS "Nullable",
                   character_maximum_length AS "Length"
            FROM information_schema.columns
            WHERE (table_name = 'Findings' AND column_name IN ('Category', 'Code', 'Quote'))
               OR (table_name = 'Runs' AND column_name = 'FactCheckMode')
            """).ToListAsync();

    private AtelierDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AtelierDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options);

    private static async Task RunUpAsync(AtelierDbContext db)
    {
        foreach (var sql in new Step50HonestWriting().UpOperations.OfType<SqlOperation>().Select(o => o.Sql))
            await db.Database.ExecuteSqlRawAsync(sql.Replace("{", "{{").Replace("}", "}}"));
    }

    private static async Task RunDownAsync(AtelierDbContext db)
    {
        foreach (var sql in new Step50HonestWriting().DownOperations.OfType<SqlOperation>().Select(o => o.Sql))
            await db.Database.ExecuteSqlRawAsync(sql.Replace("{", "{{").Replace("}", "}}"));
    }

    private sealed record ColumnRow(string Table, string Name, string Type, string Nullable, int? Length);
}
