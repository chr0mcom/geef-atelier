using Geef.Atelier.Core.Domain.Crew.Specialization;
using Geef.Atelier.Tests.Fakes;

namespace Geef.Atelier.Tests.Application.Crew;

/// <summary>PS-C2: the binding query must exclude foreign TaskBound and type-/domain-mismatched packs.</summary>
public sealed class SpecializationPackBindingScopeTests
{
    private static SpecializationPack Pack(
        string name, PackScope scope, string? domain, string? owner, PackActorType type) =>
        new(name, name, "", "delta", scope, domain, [type], owner, IsSystem: false);

    [Fact]
    public async Task ListForBinding_ExcludesForeignTaskBound_IncludesOwn()
    {
        var repo = new InMemorySpecializationPackRepository();
        await repo.UpsertAsync(Pack("own-tb", PackScope.TaskBound, null, "crew-a", PackActorType.Reviewer));
        await repo.UpsertAsync(Pack("foreign-tb", PackScope.TaskBound, null, "crew-b", PackActorType.Reviewer));

        var result = await repo.ListForBindingAsync(PackActorType.Reviewer, crewDomain: null, owningCrewId: "crew-a");

        Assert.Contains(result, p => p.Name == "own-tb");
        Assert.DoesNotContain(result, p => p.Name == "foreign-tb");
    }

    [Fact]
    public async Task ListForBinding_DomainScoped_OnlyMatchingDomain()
    {
        var repo = new InMemorySpecializationPackRepository();
        await repo.UpsertAsync(Pack("legal-only", PackScope.DomainScoped, "legal", null, PackActorType.Reviewer));

        var legal = await repo.ListForBindingAsync(PackActorType.Reviewer, "legal", null);
        var marketing = await repo.ListForBindingAsync(PackActorType.Reviewer, "marketing", null);

        Assert.Contains(legal, p => p.Name == "legal-only");
        Assert.DoesNotContain(marketing, p => p.Name == "legal-only");
    }

    [Fact]
    public async Task ListForBinding_FiltersByActorType()
    {
        var repo = new InMemorySpecializationPackRepository();
        await repo.UpsertAsync(Pack("exec-pack", PackScope.General, null, null, PackActorType.Executor));

        var forReviewer = await repo.ListForBindingAsync(PackActorType.Reviewer, null, null);
        var forExecutor = await repo.ListForBindingAsync(PackActorType.Executor, null, null);

        Assert.DoesNotContain(forReviewer, p => p.Name == "exec-pack");
        Assert.Contains(forExecutor, p => p.Name == "exec-pack");
    }

    // ── PS-E3: auto-archival ──────────────────────────────────────────────────

    [Fact]
    public async Task ArchiveUnused_ArchivesStaleUnreferenced_KeepsReferencedAndRecent()
    {
        var repo = new InMemorySpecializationPackRepository();
        await repo.UpsertAsync(Pack("stale", PackScope.General, null, null, PackActorType.Reviewer)
            with { LastUsedAt = DateTimeOffset.UtcNow.AddDays(-200) });
        await repo.UpsertAsync(Pack("referenced", PackScope.General, null, null, PackActorType.Reviewer)
            with { LastUsedAt = DateTimeOffset.UtcNow.AddDays(-200) });
        await repo.UpsertAsync(Pack("recent", PackScope.General, null, null, PackActorType.Reviewer)
            with { LastUsedAt = DateTimeOffset.UtcNow.AddDays(-1) });

        var cutoff = DateTimeOffset.UtcNow.AddDays(-90);
        var archived = await repo.ArchiveUnusedAsync(cutoff, new[] { "referenced" });

        Assert.Contains("stale", archived);
        Assert.DoesNotContain("referenced", archived);
        Assert.DoesNotContain("recent", archived);
        Assert.True((await repo.GetByNameAsync("stale"))!.Archived);
        Assert.False((await repo.GetByNameAsync("referenced"))!.Archived);
    }
}
