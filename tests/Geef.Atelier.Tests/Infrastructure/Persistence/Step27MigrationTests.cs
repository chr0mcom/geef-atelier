using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Tests.Infrastructure.Persistence;

[Collection("Postgres")]
public sealed class Step27MigrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task GroundingActorCosts_TableExists_AndAcceptsValues()
    {
        await using var db = fixture.NewContext();

        // Insert a parent run first.
        var runId = Guid.NewGuid();
        await db.Database.ExecuteSqlRawAsync(
            @"INSERT INTO ""Runs"" (""Id"",""BriefingText"",""ConfigJson"",""Status"",""CreatedAt"",""TokensTotal"",""CostTotal"")
              VALUES ({0},'step27 test',{1}::jsonb,'Pending',NOW(),0,0)",
            runId, "{}");

        var costId = Guid.NewGuid();
        await db.Database.ExecuteSqlRawAsync(
            @"INSERT INTO ""GroundingActorCosts""
                (""Id"",""RunId"",""GroundingProviderName"",""ActorName"",""ProviderName"",""ModelName"",""InputTokens"",""OutputTokens"",""CostEur"",""CreatedAt"")
              VALUES ({0},{1},'tavily-test','GroundingRefiner','openrouter','gpt-4o',50,30,0.001,NOW())",
            costId, runId);

        var count = await db.GroundingActorCosts.CountAsync(c => c.Id == costId);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GroundingActorCosts_GroundingProviderName_ColumnExists()
    {
        await using var db = fixture.NewContext();

        var names = await db.Database
            .SqlQueryRaw<string?>(@"SELECT ""GroundingProviderName"" FROM ""GroundingActorCosts"" LIMIT 1")
            .ToListAsync();

        Assert.NotNull(names);
    }

    [Fact]
    public async Task GroundingConsultations_RefinementOutcome_ColumnExists()
    {
        await using var db = fixture.NewContext();

        // Query the column to confirm it exists (will throw if column is absent).
        var values = await db.Database
            .SqlQueryRaw<string?>(@"SELECT ""RefinementOutcome""::text FROM ""GroundingConsultations"" LIMIT 1")
            .ToListAsync();

        Assert.NotNull(values);
    }

    [Fact]
    public async Task Index_IX_GroundingActorCosts_RunId_Exists()
    {
        await using var db = fixture.NewContext();

        var count = await db.Database
            .SqlQueryRaw<int>(
                @"SELECT COUNT(*)::int AS ""Value"" FROM pg_indexes
                  WHERE tablename = 'GroundingActorCosts' AND indexname = 'IX_GroundingActorCosts_RunId'")
            .FirstOrDefaultAsync();

        Assert.Equal(1, count);
    }
}
