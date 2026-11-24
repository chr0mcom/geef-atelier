using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Warnings;
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
/// Step47 against real Postgres: a database migrated up to Step46 (with system grounding rows that still carry a
/// <c>ToolName</c>) is upgraded, the statements run a second time without error, and a fully migrated database
/// accepts them again unchanged.
/// </summary>
[Trait("Category", "Integration")]
public sealed class Step47EngineHonestyMigrationTests : IAsyncLifetime
{
    private const string Step46 = "20261122120000_Step46ModelMatrixAssignments";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task UpgradesAPartlyMigratedDatabaseAndClearsToolNamesOfTheFiveSystemProfiles()
    {
        await using (var db = NewContext())
            await db.GetService<IMigrator>().MigrateAsync(Step46);

        await using (var db = NewContext())
        {
            foreach (var name in Step47EngineHonesty.LegacyGroundingProfiles)
                await SeedGroundingAsync(db, name, isSystem: true, toolName: "web-search");
            await SeedGroundingAsync(db, "crew-design-rules", isSystem: true, toolName: "static-context");
            await SeedGroundingAsync(db, "custom-tool-search", isSystem: false, toolName: "web-search");
            await SeedGroundingAsync(db, "custom-run-attachments-copy", isSystem: false, toolName: "knowledge-base");
        }

        await using (var db = NewContext())
            await db.Database.MigrateAsync();

        await using (var db = NewContext())
        {
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            foreach (var name in Step47EngineHonesty.LegacyGroundingProfiles)
                Assert.Null(await ToolNameAsync(db, name));
            Assert.Equal("static-context", await ToolNameAsync(db, "crew-design-rules"));
            Assert.Equal("web-search", await ToolNameAsync(db, "custom-tool-search"));
            Assert.Equal("knowledge-base", await ToolNameAsync(db, "custom-run-attachments-copy"));

            await RunUpAsync(db);
            await RunUpAsync(db);

            await AssertSchemaAsync(db);
        }
    }

    [Fact]
    public async Task AddsTheForeignKeyAlthoughAnotherTableHasAConstraintWithTheSameName()
    {
        await using (var db = NewContext())
            await db.GetService<IMigrator>().MigrateAsync(Step46);

        await using (var db = NewContext())
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE "UnrelatedRunNotes" (
                    "Id"    uuid NOT NULL PRIMARY KEY,
                    "RunId" uuid NOT NULL,
                    CONSTRAINT "FK_RunWarnings_Runs_RunId" FOREIGN KEY ("RunId") REFERENCES "Runs"("Id"));
                """);
        }

        await using (var db = NewContext())
            await db.Database.MigrateAsync();

        await using (var db = NewContext())
        {
            await RunUpAsync(db);
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>("""
                SELECT count(*)::int AS "Value" FROM pg_constraint
                WHERE conname = 'FK_RunWarnings_Runs_RunId' AND conrelid = '"RunWarnings"'::regclass
                """).SingleAsync());
        }

        var runId = Guid.NewGuid();
        await using (var db = NewContext())
        {
            db.Runs.Add(new RunEntity { Id = runId, CreatedAt = DateTimeOffset.UtcNow, Status = RunStatus.Failed, BriefingText = "b", ConfigJson = "{}" });
            db.RunWarnings.Add(new RunWarningEntity
            {
                Id = Guid.NewGuid(), RunId = runId, Source = RunWarningSources.Grounding, Code = RunWarningCodes.GroundingProviderSkipped,
                Message = "m", CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
            await db.Runs.Where(r => r.Id == runId).ExecuteDeleteAsync();
            Assert.Equal(0, await db.RunWarnings.CountAsync(w => w.RunId == runId));
        }
    }

    [Fact]
    public async Task RunsTwiceOnAFreshDatabaseAndWarningsCascadeWithTheirRun()
    {
        await using (var db = NewContext())
            await db.Database.MigrateAsync();

        await using (var db = NewContext())
        {
            await RunUpAsync(db);
            await RunUpAsync(db);
            await AssertSchemaAsync(db);
        }

        var runId = Guid.NewGuid();
        await using (var db = NewContext())
        {
            db.Runs.Add(new RunEntity
            {
                Id = runId,
                CreatedAt = DateTimeOffset.UtcNow,
                Status = RunStatus.Failed,
                BriefingText = "b",
                ConfigJson = "{}",
                Outcome = RunOutcome.DeliveredWithOpenPoints,
                DeliveredIteration = 2,
                ReportJson = """{"k":1}""",
            });
            db.RunWarnings.Add(new RunWarningEntity
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                Iteration = 1,
                Source = RunWarningSources.Grounding,
                Code = RunWarningCodes.GroundingProviderSkipped,
                Message = "A source could not be read and was skipped.",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var run = await db.Runs.AsNoTracking().SingleAsync(r => r.Id == runId);
            Assert.Equal((RunOutcome?)RunOutcome.DeliveredWithOpenPoints, run.Outcome);
            Assert.Equal(2, run.DeliveredIteration);
            Assert.Equal(1, await db.Database.SqlQuery<int>(
                $"""SELECT "Outcome" AS "Value" FROM "Runs" WHERE "Id" = {runId}""").SingleAsync());

            await db.Runs.Where(r => r.Id == runId).ExecuteDeleteAsync();
            Assert.Equal(0, await db.RunWarnings.CountAsync(w => w.RunId == runId));
        }
    }

    private AtelierDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AtelierDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options);

    private static async Task RunUpAsync(AtelierDbContext db)
    {
        foreach (var sql in new Step47EngineHonesty().UpOperations.OfType<SqlOperation>().Select(o => o.Sql))
            await db.Database.ExecuteSqlRawAsync(sql.Replace("{", "{{").Replace("}", "}}"));
    }

    private static async Task AssertSchemaAsync(AtelierDbContext db)
    {
        var columns = await db.Database.SqlQueryRaw<string>("""
            SELECT column_name AS "Value" FROM information_schema.columns
            WHERE table_name = 'Runs' AND column_name IN ('Outcome', 'DeliveredIteration', 'ReportJson')
            """).ToListAsync();
        Assert.Equal(["DeliveredIteration", "Outcome", "ReportJson"], columns.Order().ToList());

        var outcomeType = await db.Database.SqlQueryRaw<string>("""
            SELECT data_type AS "Value" FROM information_schema.columns WHERE table_name = 'Runs' AND column_name = 'Outcome'
            """).SingleAsync();
        Assert.Equal("integer", outcomeType);

        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_constraint
            WHERE conname = 'FK_RunWarnings_Runs_RunId' AND conrelid = '"RunWarnings"'::regclass
            """).SingleAsync());
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_indexes WHERE indexname = 'IX_RunWarnings_RunId'
            """).SingleAsync());
    }

    private static Task<string?> ToolNameAsync(AtelierDbContext db, string name) =>
        db.Database.SqlQuery<string?>(
            $"""SELECT "ToolName" AS "Value" FROM "GroundingProviderProfiles" WHERE "Name" = {name}""").SingleAsync();

    private static Task SeedGroundingAsync(AtelierDbContext db, string name, bool isSystem, string toolName) =>
        db.Database.ExecuteSqlAsync($"""
            INSERT INTO "GroundingProviderProfiles" ("Name","DisplayName","Description","ProviderType","ProviderSettings","IsSystem","ToolName")
            VALUES ({name},{name},'step47 seed','tavily',jsonb_build_object(),{isSystem},{toolName})
            ON CONFLICT ("Name") DO UPDATE SET "IsSystem" = EXCLUDED."IsSystem", "ToolName" = EXCLUDED."ToolName"
            """);
}
