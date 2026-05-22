using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Testcontainers.PostgreSql;

namespace Geef.Atelier.Tests.Persistence;

/// <summary>
/// Verifies that the Step22Finalizers migration applies cleanly, creates all expected tables,
/// and seeds the 17 system finalizer profiles.
/// </summary>
[Trait("Category", "Integration")]
public sealed class Step22FinalizersMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private AtelierDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AtelierDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new AtelierDbContext(options);
    }

    [Fact]
    public async Task Step22Migration_AppliesCleanly_NoPendingMigrationsAfter()
    {
        await using var context = NewContext();
        await context.Database.MigrateAsync();

        var pending = await context.Database.GetPendingMigrationsAsync();
        Assert.Empty(pending);
    }

    [Fact]
    public async Task Step22Migration_Seeds17SystemFinalizerProfiles()
    {
        await using var context = NewContext();
        await context.Database.MigrateAsync();

        var count = await context.FinalizerProfiles
            .Where(p => p.IsSystem)
            .CountAsync();

        // Step22 seeds 17; Step30 (D-054) adds 2 more (learning-extractor + learning-publisher)
        Assert.Equal(19, count);
    }

    [Fact]
    public async Task Step22Migration_IsIdempotent_SeedDoesNotDuplicate()
    {
        await using var context = NewContext();
        await context.Database.MigrateAsync();
        await context.Database.MigrateAsync();

        // Step22 seeds 17; Step30 (D-054) adds 2 more (learning-extractor + learning-publisher)
        var count = await context.FinalizerProfiles.CountAsync();
        Assert.Equal(19, count);
    }

    [Fact]
    public async Task Step22Migration_CreatesRunArtifactsTable()
    {
        await using var context = NewContext();
        await context.Database.MigrateAsync();

        var count = await context.RunArtifacts.CountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Step22Migration_AllSystemFinalizerProfiles_HaveCorrectTypes()
    {
        await using var context = NewContext();
        await context.Database.MigrateAsync();

        var profiles = await context.FinalizerProfiles
            .AsNoTracking()
            .Where(p => p.IsSystem)
            .ToListAsync();

        Assert.Equal(6, profiles.Count(p => p.FinalizerType == FinalizerType.FileExport));
        Assert.Equal(3, profiles.Count(p => p.FinalizerType == FinalizerType.MetadataEnrich));
        Assert.Equal(2, profiles.Count(p => p.FinalizerType == FinalizerType.ExternalSink));
        Assert.Equal(6, profiles.Count(p => p.FinalizerType == FinalizerType.Transform));
    }
}
