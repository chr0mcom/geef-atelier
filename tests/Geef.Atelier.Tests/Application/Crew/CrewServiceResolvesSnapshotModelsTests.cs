using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.Models;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Tests.Fakes;

namespace Geef.Atelier.Tests.Application.Crew;

/// <summary>
/// Verifies the wiring, not just the helper: a snapshot produced by <see cref="CrewService.ResolveCodeTemplateSnapshotAsync"/> is built
/// from the code-only profiles of the template, takes the models of the model assignment and carries concrete model ids, so an
/// always-latest alias of the matrix is pinned for the run.
/// </summary>
public sealed class CrewServiceResolvesSnapshotModelsTests
{
    private const string OpusAlias = "claude-opus-latest";
    private const string OpusConcrete = "claude-opus-5";
    private const string SonnetAlias = "claude-sonnet-latest";
    private const string SonnetConcrete = "claude-sonnet-5";

    private static (CrewService Service, ResolvingModelCatalog Catalog) Build()
    {
        var catalog = new ResolvingModelCatalog(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [OpusAlias] = OpusConcrete,
            [SonnetAlias] = SonnetConcrete,
        });
        return (new CrewService(catalog, new AliasingModelAssignment()), catalog);
    }

    [Theory]
    [InlineData(SystemCrew.DirectTemplateName)]
    public async Task SnapshotIsBuiltFromTheCodeOnlyProfilesOfTheTemplate(string templateName)
    {
        var (service, _) = Build();
        var template = SystemCrew.CodeOnlyTemplates[templateName];

        var resolved = await service.ResolveCodeTemplateSnapshotAsync(template);

        Assert.Equal(templateName, resolved.Snapshot.TemplateName);
        Assert.Equal(template.ExecutorProfileName, resolved.Snapshot.Executor.Name);
        Assert.Equal(template.ReviewerProfileNames, resolved.Snapshot.Reviewers.Select(r => r.Name));
        Assert.Empty(resolved.Snapshot.Advisors);
    }

    [Fact]
    public async Task SnapshotCarriesTheConcreteModelForEveryActor()
    {
        var (service, _) = Build();

        var resolved = await service.ResolveCodeTemplateSnapshotAsync(SystemCrew.DirectTemplate);

        Assert.Equal(OpusConcrete, resolved.Snapshot.Executor.Model);
        Assert.All(resolved.Snapshot.Reviewers.Where(r => r.Model.Length > 0), r => Assert.Equal(SonnetConcrete, r.Model));
    }

    [Fact]
    public async Task ResolutionActuallyGoesThroughTheCatalog()
    {
        var (service, catalog) = Build();

        await service.ResolveCodeTemplateSnapshotAsync(SystemCrew.DirectTemplate);

        Assert.Contains(OpusAlias, catalog.ResolveCalls);
        Assert.Contains(SonnetAlias, catalog.ResolveCalls);
    }

    [Fact]
    public async Task WarningsOfTheModelAssignmentAreHandedOn()
    {
        var (service, _) = Build();

        var resolved = await service.ResolveCodeTemplateSnapshotAsync(SystemCrew.DirectTemplate);

        Assert.Equal(AliasingModelAssignment.Warning, Assert.Single(resolved.Warnings));
    }

    private sealed class AliasingModelAssignment : IModelAssignmentService
    {
        public static readonly ModelAssignmentWarning Warning = new("test-writer", "Assigned an alias for the test.");

        private readonly PassThroughModelAssignmentService _inner = new();

        public Task<ModelAssignment> AssignAsync(ModelSlot slot, ModelDomain domain, ModelCandidate? executor = null, CancellationToken ct = default) =>
            _inner.AssignAsync(slot, domain, executor, ct);

        public Task<IReadOnlyList<ModelAssignment>> AssignReviewersAsync(
            IReadOnlyList<ReviewerAssignmentRequest> reviewers, ModelDomain crewDomain, ModelCandidate executor, CancellationToken ct = default) =>
            _inner.AssignReviewersAsync(reviewers, crewDomain, executor, ct);

        public Task<CrewSnapshot> ApplyAsync(CrewSnapshot snapshot, CancellationToken ct = default) => ApplyAsync(snapshot, [], ct);

        public Task<CrewSnapshot> ApplyAsync(CrewSnapshot snapshot, ICollection<ModelAssignmentWarning> warnings, CancellationToken ct = default)
        {
            warnings.Add(Warning);
            return Task.FromResult(snapshot with
            {
                Executor = snapshot.Executor with { Model = OpusAlias },
                Reviewers = snapshot.Reviewers.Select(r => r.Model.Length == 0 ? r : r with { Model = SonnetAlias }).ToList(),
            });
        }
    }
}
