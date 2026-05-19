using Geef.Atelier.Core.Domain.Dashboard;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Persistence.Dashboard;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Tests.Persistence;

[Collection("Postgres")]
public sealed class DashboardRepositoryTests(PostgresFixture fixture)
{
    private static async Task CreateCompletedRun(AtelierDbContext db, string user, string? templateName = null)
    {
        var id = Guid.NewGuid();
        // Include all NOT NULL columns with sensible defaults
        if (templateName is not null)
        {
            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""Runs"" (""Id"",""BriefingText"",""ConfigJson"",""Status"",
                                       ""CreatedAt"",""CompletedAt"",""CreatedByUser"",
                                       ""CrewTemplateName"",""FinalText"",""WordCount"",
                                       ""TokensTotal"",""CostTotal"")
                  VALUES ({0},{1},{2}::jsonb,'Completed',NOW()-INTERVAL '1 hour',NOW(),{3},{4},{5},2,0,0)",
                id, "test briefing", "{}", user, templateName, "hello world");
        }
        else
        {
            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""Runs"" (""Id"",""BriefingText"",""ConfigJson"",""Status"",
                                       ""CreatedAt"",""CompletedAt"",""CreatedByUser"",
                                       ""FinalText"",""WordCount"",
                                       ""TokensTotal"",""CostTotal"")
                  VALUES ({0},{1},{2}::jsonb,'Completed',NOW()-INTERVAL '1 hour',NOW(),{3},{4},2,0,0)",
                id, "test briefing", "{}", user, "hello world");
        }
    }

    [Fact]
    public async Task GetWelcomeStripAsync_MyScope_ReturnsUserData()
    {
        await using var db = fixture.NewContext();
        var repo = new DashboardRepository(db);
        var user = $"dash-test-{Guid.NewGuid():N}";

        await CreateCompletedRun(db, user);
        await CreateCompletedRun(db, user);

        var strip = await repo.GetWelcomeStripAsync(user, false, DashboardScope.My, CancellationToken.None);

        Assert.Equal(user, strip.Username);
        Assert.True(strip.TotalManuscripts >= 2, $"Expected >=2, got {strip.TotalManuscripts}");
    }

    [Fact]
    public async Task GetWelcomeStripAsync_EmptyUser_ReturnsZeroCounts()
    {
        await using var db = fixture.NewContext();
        var repo = new DashboardRepository(db);
        var user = $"empty-{Guid.NewGuid():N}";

        var strip = await repo.GetWelcomeStripAsync(user, false, DashboardScope.My, CancellationToken.None);

        Assert.Equal(0, strip.TotalManuscripts);
        Assert.Equal(0, strip.TodayCount);
        Assert.Equal(0, strip.StreakDays);
    }

    [Fact]
    public async Task GetPressStatusAsync_NoActiveRuns_ReturnsIdle()
    {
        await using var db = fixture.NewContext();
        var repo = new DashboardRepository(db);
        var user = $"press-{Guid.NewGuid():N}";

        // Null = All-scope; this user has no runs anyway
        var press = await repo.GetPressStatusAsync(user, CancellationToken.None);

        Assert.Equal(PressState.Idle, press.State);
    }

    [Fact]
    public async Task GetLedgerStatsAsync_AllScope_IncludesSeededRun()
    {
        await using var db = fixture.NewContext();
        var repo = new DashboardRepository(db);
        var user = $"ledger-{Guid.NewGuid():N}";

        await CreateCompletedRun(db, user);

        // All scope (null username)
        var allLedger = await repo.GetLedgerStatsAsync(null, CancellationToken.None);
        var myLedger  = await repo.GetLedgerStatsAsync(user, CancellationToken.None);

        Assert.True(allLedger.AllTime.RunCount >= myLedger.AllTime.RunCount,
            $"All={allLedger.AllTime.RunCount} should be >= My={myLedger.AllTime.RunCount}");
        Assert.True(myLedger.AllTime.RunCount >= 1,
            $"My ledger should contain at least 1 run, got {myLedger.AllTime.RunCount}");
    }

    [Fact]
    public async Task GetManuscriptsAsync_ReturnsCompletedRunsForUser()
    {
        await using var db = fixture.NewContext();
        var repo = new DashboardRepository(db);
        var user = $"ms-{Guid.NewGuid():N}";

        await CreateCompletedRun(db, user, "klassik");

        // Use user-scoped query
        var manuscripts = await repo.GetManuscriptsAsync(user, CancellationToken.None);

        Assert.Contains(manuscripts, m => m.TemplateName == "klassik");
    }

    [Fact]
    public async Task GetDayBookAsync_MaxTwelveEntries()
    {
        await using var db = fixture.NewContext();
        var repo = new DashboardRepository(db);
        var user = $"db-{Guid.NewGuid():N}";

        var entries = await repo.GetDayBookAsync(user, false, DashboardScope.My, CancellationToken.None);

        Assert.True(entries.Count <= 12, $"Expected <=12, got {entries.Count}");
    }
}
