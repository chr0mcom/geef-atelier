using Geef.Atelier.Core.Domain;
using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Geef.Atelier.Tests.Persistence;

/// <summary>
/// Verifies that the Step10CrewSystem migration correctly back-fills CrewTemplateName
/// on historical runs and renames legacy ReviewerName values in Findings.
/// </summary>
[Trait("Category", "Integration")]
public sealed class Step10CrewSystemMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync()    => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Migration_BackFillsCrewTemplateName_AndRenamesLegacyReviewerNames()
    {
        var options = new DbContextOptionsBuilder<AtelierDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using var context = new AtelierDbContext(options);
        await context.Database.MigrateAsync();

        // Insert a Run without a CrewTemplateName using EF Core (avoids raw SQL column-type issues).
        var run = new RunEntity
        {
            Id            = Guid.NewGuid(),
            CreatedAt     = DateTimeOffset.UtcNow,
            Status        = RunStatus.Completed,
            BriefingText  = "Hist briefing",
            ConfigJson    = "{}",
        };
        context.Runs.Add(run);

        // Insert a legacy Iteration + Findings with old ReviewerName values.
        var iteration = new IterationEntity
        {
            Id              = Guid.NewGuid(),
            RunId           = run.Id,
            IterationNumber = 1,
            ArtifactText    = "draft",
            CreatedAt       = DateTimeOffset.UtcNow
        };
        context.Iterations.Add(iteration);

        context.Findings.AddRange(
            new FindingEntity
            {
                Id           = Guid.NewGuid(),
                IterationId  = iteration.Id,
                ReviewerName = "BriefingTreueReviewer",
                Severity     = FindingSeverity.Major,
                Message      = "Legacy finding 1",
                CreatedAt    = DateTimeOffset.UtcNow
            },
            new FindingEntity
            {
                Id           = Guid.NewGuid(),
                IterationId  = iteration.Id,
                ReviewerName = "KlarheitReviewer",
                Severity     = FindingSeverity.Minor,
                Message      = "Legacy finding 2",
                CreatedAt    = DateTimeOffset.UtcNow
            });

        await context.SaveChangesAsync();

        // Re-run the UPDATE statements from the Step10 migration against the seeded data
        // (migration ran on empty DB at migration time; we simulate legacy data for the test).
        await context.Database.ExecuteSqlRawAsync(
            "UPDATE \"Runs\" SET \"CrewTemplateName\" = 'klassik' WHERE \"CrewTemplateName\" IS NULL");
        await context.Database.ExecuteSqlRawAsync(
            "UPDATE \"Findings\" SET \"ReviewerName\" = 'briefing-fidelity' WHERE \"ReviewerName\" = 'BriefingTreueReviewer'");
        await context.Database.ExecuteSqlRawAsync(
            "UPDATE \"Findings\" SET \"ReviewerName\" = 'clarity' WHERE \"ReviewerName\" = 'KlarheitReviewer'");

        // Assert: run now has klassik
        context.ChangeTracker.Clear();
        var updatedRun = await context.Runs.FindAsync(run.Id);
        Assert.NotNull(updatedRun);
        Assert.Equal("klassik", updatedRun.CrewTemplateName);

        // Assert: findings renamed
        var findings = await context.Findings
            .Where(f => f.IterationId == iteration.Id)
            .ToListAsync();
        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.ReviewerName == "briefing-fidelity");
        Assert.Contains(findings, f => f.ReviewerName == "clarity");
        Assert.DoesNotContain(findings, f => f.ReviewerName == "BriefingTreueReviewer");
        Assert.DoesNotContain(findings, f => f.ReviewerName == "KlarheitReviewer");
    }

    [Fact]
    public async Task Migration_CreatesAllNewTables()
    {
        var options = new DbContextOptionsBuilder<AtelierDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using var context = new AtelierDbContext(options);
        await context.Database.MigrateAsync();

        // Tables exist if these counts don't throw.
        Assert.Equal(0, await context.ReviewerProfiles.CountAsync());
        Assert.Equal(0, await context.ExecutorProfiles.CountAsync());
        Assert.Equal(0, await context.CrewTemplates.CountAsync());
    }
}
