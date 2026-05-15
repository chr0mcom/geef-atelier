using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Persistence.Crew;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Geef.Atelier.Tests.Persistence;

/// <summary>
/// Regression: in Blazor Server the scoped <see cref="AtelierDbContext"/> lives for the whole
/// circuit, so a user editing the same custom template twice reuses one context instance.
/// The previous <c>DbSet.Update(detachedEntity)</c> implementation threw
/// "another instance with the same key value is already being tracked" on the second save.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CrewTemplateRepositoryReeditTests : IAsyncLifetime
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
            .Options;
        return new AtelierDbContext(options);
    }

    private static CrewTemplate Template(string displayName) => new(
        Name: "custom-klassik-copy",
        DisplayName: displayName,
        Description: "RAG Test",
        ExecutorProfileName: "default-executor",
        ReviewerProfileNames: ["briefing-fidelity"],
        EvaluationStrategy: EvaluationStrategy.Parallel,
        ConvergenceOverride: null,
        AdvisorProfileNames: [],
        GroundingProviderNames: ["tavily-basic"],
        IsSystem: false);

    [Fact]
    public async Task UpdateAsync_TwiceOnSameContext_DoesNotThrowTrackingConflict()
    {
        await using var setup = NewContext();
        await setup.Database.MigrateAsync();

        // One shared context for the whole "circuit".
        await using var circuit = NewContext();
        var repo = new CrewTemplateRepository(circuit);

        await repo.CreateAsync(Template("RAG Test"));

        // First edit + save.
        await repo.UpdateAsync(Template("RAG Test v2"));

        // Second edit + save on the SAME context — used to throw the tracking conflict.
        await repo.UpdateAsync(Template("RAG Test v3"));

        await using var verify = NewContext();
        var persisted = await verify.CrewTemplates
            .AsNoTracking()
            .FirstAsync(t => t.Name == "custom-klassik-copy");
        Assert.Equal("RAG Test v3", persisted.DisplayName);
    }

    [Fact]
    public async Task UpdateAsync_NonExistentTemplate_ThrowsClearError()
    {
        await using var setup = NewContext();
        await setup.Database.MigrateAsync();

        await using var ctx = NewContext();
        var repo = new CrewTemplateRepository(ctx);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repo.UpdateAsync(Template("missing")));
        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
