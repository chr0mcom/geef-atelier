using Geef.Atelier.Application.Crew;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Application.Crew;

/// <summary>
/// Verifies the wiring, not just the helper: a snapshot produced by
/// <see cref="CrewService.ResolveSnapshotAsync"/> carries concrete model ids, while the stored
/// profile keeps its always-latest alias so the next run still follows new releases.
/// </summary>
public sealed class CrewServiceResolvesSnapshotModelsTests
{
    private const string OpusAlias = "claude-opus-latest";
    private const string OpusConcrete = "claude-opus-5";
    private const string SonnetAlias = "claude-sonnet-latest";
    private const string SonnetConcrete = "claude-sonnet-5";

    private static readonly ExecutorProfile AliasedExecutor =
        new("exec", "Exec", "d", "p", "claude-cli", OpusAlias, 32000, false);

    private static readonly ReviewerProfile AliasedReviewer =
        new("rev", "Rev", "d", "p", "claude-cli", SonnetAlias, 8000, false);

    private static (CrewService Service, ResolvingModelCatalog Catalog) Build()
    {
        var catalog = new ResolvingModelCatalog(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [OpusAlias] = OpusConcrete,
            [SonnetAlias] = SonnetConcrete,
        });

        var service = new CrewService(
            new OneReviewerRepo(),
            new OneExecutorRepo(),
            new NoAdvisorRepo(),
            new NoGroundingRepo(),
            new InMemoryFinalizerProfileRepository(),
            new NoTemplateRepo(),
            new InMemorySpecializationPackRepository(),
            catalog,
            NullLogger<CrewService>.Instance);

        return (service, catalog);
    }

    private static CrewSpec Spec() =>
        new(AliasedExecutor.Name, [AliasedReviewer.Name], EvaluationStrategy.Parallel, null);

    [Fact]
    public async Task SnapshotCarriesTheConcreteModelForEveryActor()
    {
        var (service, _) = Build();

        var snapshot = await service.ResolveSnapshotAsync(crewTemplateName: null, customCrew: Spec());

        Assert.Equal(OpusConcrete, snapshot.Executor.Model);
        Assert.Equal(SonnetConcrete, Assert.Single(snapshot.Reviewers).Model);
    }

    [Fact]
    public async Task TheStoredProfileKeepsItsAlias()
    {
        var (service, _) = Build();

        await service.ResolveSnapshotAsync(crewTemplateName: null, customCrew: Spec());

        var stored = await service.GetExecutorProfileAsync(AliasedExecutor.Name);
        Assert.Equal(OpusAlias, stored!.Model);
    }

    [Fact]
    public async Task ResolutionActuallyGoesThroughTheCatalog()
    {
        var (service, catalog) = Build();

        await service.ResolveSnapshotAsync(crewTemplateName: null, customCrew: Spec());

        Assert.Contains(OpusAlias, catalog.ResolveCalls);
        Assert.Contains(SonnetAlias, catalog.ResolveCalls);
    }

    private sealed class OneExecutorRepo : IExecutorProfileRepository
    {
        public Task<IReadOnlyList<ExecutorProfile>> ListAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ExecutorProfile>>([AliasedExecutor]);
        public Task<ExecutorProfile?> GetByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult(name == AliasedExecutor.Name ? AliasedExecutor : null);
        public Task UpsertAsync(ExecutorProfile profile, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task RenameAsync(string oldName, string newName, CancellationToken ct = default) => Task.CompletedTask;
        public Task CreateAsync(ExecutorProfile item, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(ExecutorProfile item, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class OneReviewerRepo : IReviewerProfileRepository
    {
        public Task<IReadOnlyList<ReviewerProfile>> ListAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ReviewerProfile>>([AliasedReviewer]);
        public Task<ReviewerProfile?> GetByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult(name == AliasedReviewer.Name ? AliasedReviewer : null);
        public Task UpsertAsync(ReviewerProfile profile, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task RenameAsync(string oldName, string newName, CancellationToken ct = default) => Task.CompletedTask;
        public Task CreateAsync(ReviewerProfile item, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(ReviewerProfile item, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoAdvisorRepo : IAdvisorProfileRepository
    {
        public Task<IReadOnlyList<AdvisorProfile>> ListAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AdvisorProfile>>([]);
        public Task<AdvisorProfile?> GetByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult<AdvisorProfile?>(null);
        public Task UpsertAsync(AdvisorProfile profile, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task RenameAsync(string oldName, string newName, CancellationToken ct = default) => Task.CompletedTask;
        public Task CreateAsync(AdvisorProfile item, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(AdvisorProfile item, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoGroundingRepo : IGroundingProviderProfileRepository
    {
        public Task<IReadOnlyList<GroundingProviderProfile>> ListAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GroundingProviderProfile>>([]);
        public Task<GroundingProviderProfile?> GetByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult<GroundingProviderProfile?>(null);
        public Task UpsertAsync(GroundingProviderProfile profile, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task RenameAsync(string oldName, string newName, CancellationToken ct = default) => Task.CompletedTask;
        public Task<GroundingProviderProfile> CreateAsync(GroundingProviderProfile item, CancellationToken ct = default)
            => Task.FromResult(item);
        public Task<GroundingProviderProfile> UpdateAsync(GroundingProviderProfile item, CancellationToken ct = default)
            => Task.FromResult(item);
        public Task<IReadOnlyList<GroundingProviderProfile>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GroundingProviderProfile>>([]);
    }

    private sealed class NoTemplateRepo : ICrewTemplateRepository
    {
        public Task<IReadOnlyList<CrewTemplate>> ListAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CrewTemplate>>([]);
        public Task<CrewTemplate?> GetByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult<CrewTemplate?>(null);
        public Task UpsertAsync(CrewTemplate template, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task RenameAsync(string oldName, string newName, CancellationToken ct = default) => Task.CompletedTask;
        public Task CreateAsync(CrewTemplate item, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(CrewTemplate item, CancellationToken ct = default) => Task.CompletedTask;
    }
}
