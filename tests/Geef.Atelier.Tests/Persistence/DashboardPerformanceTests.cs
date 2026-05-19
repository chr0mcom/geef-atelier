using System.Diagnostics;
using Geef.Atelier.Core.Domain.Dashboard;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Persistence.Dashboard;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Tests.Persistence;

[Collection("Postgres")]
public sealed class DashboardPerformanceTests(PostgresFixture fixture)
{
    private const int SeedCount = 100;
    private const int MaxMs     = 3000;

    [Fact]
    public async Task GetLedgerStatsAsync_With100Runs_CompletesUnder3s()
    {
        await using var db = fixture.NewContext();
        var user = $"perf-l-{Guid.NewGuid():N}";

        await SeedCompletedRunsAsync(db, user, SeedCount);

        var repo = new DashboardRepository(db);
        var sw   = Stopwatch.StartNew();
        var result = await repo.GetLedgerStatsAsync(null, CancellationToken.None);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < MaxMs,
            $"GetLedgerStatsAsync took {sw.ElapsedMilliseconds}ms — limit {MaxMs}ms");
        _ = result; // suppress unused var
    }

    [Fact]
    public async Task GetWelcomeStripAsync_With100Runs_CompletesUnder3s()
    {
        await using var db = fixture.NewContext();
        var user = $"perf-ws-{Guid.NewGuid():N}";

        await SeedCompletedRunsAsync(db, user, SeedCount);

        var repo = new DashboardRepository(db);
        var sw   = Stopwatch.StartNew();
        var result = await repo.GetWelcomeStripAsync(user, false, DashboardScope.My, CancellationToken.None);
        sw.Stop();

        Assert.True(result.TotalManuscripts >= SeedCount,
            $"Expected at least {SeedCount} total, got {result.TotalManuscripts}");
        Assert.True(sw.ElapsedMilliseconds < MaxMs,
            $"GetWelcomeStripAsync took {sw.ElapsedMilliseconds}ms — limit {MaxMs}ms");
    }

    private static async Task SeedCompletedRunsAsync(AtelierDbContext db, string user, int count)
    {
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < count; i++)
        {
            var id          = Guid.NewGuid();
            var createdAt   = now.AddHours(-(count - i));
            var completedAt = createdAt.AddMinutes(30);
            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""Runs"" (""Id"",""BriefingText"",""ConfigJson"",""Status"",
                                       ""CreatedAt"",""CompletedAt"",""CreatedByUser"",
                                       ""FinalText"",""WordCount"",""TokensTotal"",""CostTotal"")
                  VALUES ({0},{1},{2}::jsonb,'Completed',{3},{4},{5},{6},3,0,0)",
                id, $"briefing {i}", "{}", createdAt, completedAt, user, "word count text");
        }
    }
}
