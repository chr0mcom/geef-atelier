using Geef.Atelier.Application.Crew;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.Crew.Specialization;

namespace Geef.Atelier.Tests.Application.Crew;

public sealed class CrewSnapshotPackCompositionTests
{
    private static CrewSnapshot BaseSnapshot(ReviewerProfile reviewer) => new(
        SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
        TemplateName: "t",
        Executor: new ExecutorProfile("exec", "Exec", "", "Write. {specialization}", "p", "m", null, false),
        Reviewers: [reviewer],
        EvaluationStrategy: EvaluationStrategy.Parallel,
        ConvergenceOverride: null,
        Advisors: Array.Empty<AdvisorProfile>());

    private static SpecializationPack Pack(string name, string text, PackActorType type) => new(
        name, name, "", text, PackScope.DomainScoped, "legal", [type], null, false);

    [Fact]
    public async Task ApplyPacksAsync_ComposesReviewerPrompt_AndRecordsProvenance()
    {
        var reviewer = new ReviewerProfile("r1", "R1", "", "Role. {specialization}", "p", "m", null, false);
        var snapshot = BaseSnapshot(reviewer);
        var bindings = new Dictionary<string, IReadOnlyList<string>>
        {
            ["reviewer:r1"] = new[] { "legal-terminology" }
        };
        var pack = Pack("legal-terminology", "Legal delta.", PackActorType.Reviewer);

        var result = await CrewSnapshotBuilder.ApplyPacksAsync(
            snapshot, bindings, (n, _) => Task.FromResult<SpecializationPack?>(n == pack.Name ? pack : null));

        Assert.Equal("Role. Legal delta.", result.Reviewers[0].SystemPrompt);
        Assert.NotNull(result.PromptCompositions);
        var comp = Assert.Single(result.PromptCompositions!);
        Assert.Equal("reviewer", comp.ActorType);
        Assert.Equal("r1", comp.ActorName);
        Assert.Equal("Role. {specialization}", comp.RolePrompt);
        Assert.Equal("Role. Legal delta.", comp.ComposedPrompt);
        Assert.Equal("legal-terminology", Assert.Single(comp.Packs).Name);
    }

    [Fact]
    public async Task ApplyPacksAsync_NoBindings_ReturnsSnapshotUnchanged()
    {
        var reviewer = new ReviewerProfile("r1", "R1", "", "Role. {specialization}", "p", "m", null, false);
        var snapshot = BaseSnapshot(reviewer);

        var result = await CrewSnapshotBuilder.ApplyPacksAsync(
            snapshot, new Dictionary<string, IReadOnlyList<string>>(),
            (_, _) => Task.FromResult<SpecializationPack?>(null));

        Assert.Same(snapshot, result);
        Assert.Null(result.PromptCompositions);
    }

    [Fact]
    public async Task ApplyPacksAsync_SkipsTypeIncompatiblePack()
    {
        var reviewer = new ReviewerProfile("r1", "R1", "", "Role. {specialization}", "p", "m", null, false);
        var snapshot = BaseSnapshot(reviewer);
        var bindings = new Dictionary<string, IReadOnlyList<string>>
        {
            ["reviewer:r1"] = new[] { "exec-only" }
        };
        // Pack only applies to executors — must be skipped for a reviewer.
        var pack = Pack("exec-only", "Executor delta.", PackActorType.Executor);

        var result = await CrewSnapshotBuilder.ApplyPacksAsync(
            snapshot, bindings, (n, _) => Task.FromResult<SpecializationPack?>(n == pack.Name ? pack : null));

        Assert.Equal("Role. {specialization}", result.Reviewers[0].SystemPrompt);
        Assert.Null(result.PromptCompositions);
    }
}
