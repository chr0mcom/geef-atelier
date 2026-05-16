using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Persistence.Crew;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Testcontainers.PostgreSql;

namespace Geef.Atelier.Tests.Persistence;

/// <summary>
/// Regression: editing a custom template and setting "Advanced (Convergence Override)"
/// values must round-trip through the repository. The load-then-SetValues update path
/// must not silently drop the value-converted jsonb properties.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CrewTemplateConvergenceOverrideTests : IAsyncLifetime
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

    private static CrewTemplate Template(ConvergencePolicyOverride? overr) => new(
        Name: "custom-klassik-copy",
        DisplayName: "RAG Test",
        Description: "RAG Test",
        ExecutorProfileName: "default-executor",
        ReviewerProfileNames: ["briefing-fidelity"],
        EvaluationStrategy: EvaluationStrategy.Parallel,
        ConvergenceOverride: overr,
        AdvisorProfileNames: [],
        GroundingProviderNames: ["tavily-basic"],
        IsSystem: false);

    [Fact]
    public async Task CreateAsync_WithConvergenceOverride_RoundTrips()
    {
        await using var setup = NewContext();
        await setup.Database.MigrateAsync();

        await using (var ctx = NewContext())
        {
            var repo = new CrewTemplateRepository(ctx);
            await repo.CreateAsync(Template(new ConvergencePolicyOverride(7, true, false, 3)));
        }

        await using var verify = NewContext();
        var persisted = await verify.CrewTemplates.AsNoTracking()
            .FirstAsync(t => t.Name == "custom-klassik-copy");

        Assert.NotNull(persisted.ConvergenceOverride);
        Assert.Equal(7, persisted.ConvergenceOverride!.MaxIterations);
        Assert.Equal(true, persisted.ConvergenceOverride.AbortOnCritical);
        Assert.Equal(3, persisted.ConvergenceOverride.StagnationThreshold);
    }

    [Fact]
    public async Task UpdateAsync_SetsConvergenceOverrideFromNull_RoundTrips()
    {
        await using var setup = NewContext();
        await setup.Database.MigrateAsync();

        // Simulate "Duplicate as custom" of a system template (override is null), then
        // the user opens the editor, fills the Advanced section and saves — all on the
        // one shared Blazor-circuit context.
        await using var circuit = NewContext();
        var repo = new CrewTemplateRepository(circuit);

        await repo.CreateAsync(Template(overr: null));
        await repo.UpdateAsync(Template(new ConvergencePolicyOverride(9, true, true, 4)));

        await using var verify = NewContext();
        var persisted = await verify.CrewTemplates.AsNoTracking()
            .FirstAsync(t => t.Name == "custom-klassik-copy");

        Assert.NotNull(persisted.ConvergenceOverride);
        Assert.Equal(9, persisted.ConvergenceOverride!.MaxIterations);
        Assert.Equal(true, persisted.ConvergenceOverride.AbortOnCritical);
        Assert.Equal(true, persisted.ConvergenceOverride.DetectRegression);
        Assert.Equal(4, persisted.ConvergenceOverride.StagnationThreshold);
    }

    [Fact]
    public async Task UpdateAsync_ChangesExistingConvergenceOverride_RoundTrips()
    {
        await using var setup = NewContext();
        await setup.Database.MigrateAsync();

        await using var circuit = NewContext();
        var repo = new CrewTemplateRepository(circuit);

        await repo.CreateAsync(Template(new ConvergencePolicyOverride(5, false, false, 2)));
        await repo.UpdateAsync(Template(new ConvergencePolicyOverride(12, true, true, 6)));

        await using var verify = NewContext();
        var persisted = await verify.CrewTemplates.AsNoTracking()
            .FirstAsync(t => t.Name == "custom-klassik-copy");

        Assert.NotNull(persisted.ConvergenceOverride);
        Assert.Equal(12, persisted.ConvergenceOverride!.MaxIterations);
        Assert.Equal(6, persisted.ConvergenceOverride.StagnationThreshold);
    }
}
