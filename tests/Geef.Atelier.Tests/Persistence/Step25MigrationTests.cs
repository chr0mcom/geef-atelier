using Geef.Atelier.Core.Domain;
using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Tests.Persistence;

[Collection("Postgres")]
public sealed class Step25MigrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Runs_WordCount_ColumnExists_AndAcceptsValues()
    {
        await using var db = fixture.NewContext();

        var runId = Guid.NewGuid();
        await db.Database.ExecuteSqlRawAsync(
            @"INSERT INTO ""Runs"" (""Id"",""BriefingText"",""ConfigJson"",""Status"",""CreatedAt"",""TokensTotal"",""CostTotal"")
              VALUES ({0},'wordcount test',{1}::jsonb,'Pending',NOW(),0,0)",
            runId, "{}");

        await db.Database.ExecuteSqlRawAsync(
            @"UPDATE ""Runs"" SET ""WordCount"" = 42 WHERE ""Id"" = {0}", runId);

        var wc = await db.Runs.AsNoTracking()
            .Where(r => r.Id == runId)
            .Select(r => r.WordCount)
            .FirstOrDefaultAsync();

        Assert.Equal(42, wc);
    }

    [Fact]
    public async Task IterationActorCosts_ProviderName_ColumnExists()
    {
        await using var db = fixture.NewContext();

        var names = await db.Database
            .SqlQueryRaw<string?>(@"SELECT ""ProviderName"" FROM ""IterationActorCosts"" LIMIT 1")
            .ToListAsync();

        Assert.NotNull(names);
    }

    [Fact]
    public async Task FinalizationActorCosts_ProviderName_ColumnExists()
    {
        await using var db = fixture.NewContext();

        var names = await db.Database
            .SqlQueryRaw<string?>(@"SELECT ""ProviderName"" FROM ""FinalizationActorCosts"" LIMIT 1")
            .ToListAsync();

        Assert.NotNull(names);
    }

    [Fact]
    public async Task Index_IX_Runs_CreatedAt_Exists()
    {
        await using var db = fixture.NewContext();

        var count = await db.Database
            .SqlQueryRaw<int>(
                @"SELECT COUNT(*)::int AS ""Value"" FROM pg_indexes
                  WHERE tablename = 'Runs' AND indexname = 'IX_Runs_CreatedAt'")
            .FirstOrDefaultAsync();

        Assert.Equal(1, count);
    }
}
