using Geef.Atelier.Application.Crew;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Persistence.Crew;

namespace Geef.Atelier.Tests.Application.Crew;

public sealed class CrewServiceGroundingProviderCrudTests
{
    [Fact]
    public async Task CreateCustomGroundingProviderProfileAsync_AddsCustomPrefix()
    {
        var repo = new InMemoryGroundingProviderProfileRepository();
        var svc  = BuildService(groundingRepo: repo);

        var result = await svc.CreateCustomGroundingProviderProfileAsync(BuildProfile("my-search"));

        Assert.Equal("custom-my-search", result.Name);
        Assert.False(result.IsSystem);
    }

    [Fact]
    public async Task CreateCustomGroundingProviderProfileAsync_ThrowsOnSystemName()
    {
        var svc = BuildService();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CreateCustomGroundingProviderProfileAsync(SystemCrew.TavilyBasicProfile));
    }

    [Fact]
    public async Task DeleteCustomGroundingProviderProfileAsync_ThrowsOnSystemName()
    {
        var svc = BuildService();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.DeleteCustomGroundingProviderProfileAsync(SystemCrew.TavilyBasicProfile.Name));
    }

    [Fact]
    public async Task UpdateCustomGroundingProviderProfileAsync_ThrowsOnSystemName()
    {
        var svc = BuildService();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.UpdateCustomGroundingProviderProfileAsync(SystemCrew.TavilyBasicProfile));
    }

    [Fact]
    public async Task ListGroundingProviderProfilesAsync_IncludesSystemProfiles_WhenRequested()
    {
        var repo = new InMemoryGroundingProviderProfileRepository();
        var svc  = BuildService(groundingRepo: repo);

        var all = await svc.ListGroundingProviderProfilesAsync(includeSystem: true);

        Assert.Contains(all, p => p.Name == SystemCrew.TavilyBasicProfile.Name);
    }

    [Fact]
    public async Task ListGroundingProviderProfilesAsync_ExcludesSystemProfiles_WhenNotRequested()
    {
        var repo = new InMemoryGroundingProviderProfileRepository();
        var svc  = BuildService(groundingRepo: repo);

        var all = await svc.ListGroundingProviderProfilesAsync(includeSystem: false);

        Assert.DoesNotContain(all, p => p.IsSystem);
    }

    [Fact]
    public async Task GetGroundingProviderProfileAsync_ReturnsSystemProfile()
    {
        var svc = BuildService();

        var profile = await svc.GetGroundingProviderProfileAsync(SystemCrew.TavilyBasicProfile.Name);

        Assert.NotNull(profile);
        Assert.Equal(SystemCrew.TavilyBasicProfile.Name, profile.Name);
        Assert.True(profile.IsSystem);
    }

    // --- Helpers ---

    private static ICrewService BuildService(
        InMemoryGroundingProviderProfileRepository? groundingRepo = null)
        => new CrewService(
            new InMemoryReviewerProfileRepository(),
            new InMemoryExecutorProfileRepository(),
            new InMemoryAdvisorProfileRepository(),
            groundingRepo ?? new InMemoryGroundingProviderProfileRepository(),
            new InMemoryCrewTemplateRepository());

    private static GroundingProviderProfile BuildProfile(string name) => new(
        Name: name,
        DisplayName: name,
        Description: "desc",
        ProviderType: "tavily",
        ProviderSettings: new Dictionary<string, string> { ["Tier"] = "basic" },
        MaxQueriesPerRun: 1,
        IsSystem: false);

    private sealed class InMemoryGroundingProviderProfileRepository : IGroundingProviderProfileRepository
    {
        private readonly List<GroundingProviderProfile> _store = [];

        public Task<IReadOnlyList<GroundingProviderProfile>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GroundingProviderProfile>>(_store);

        public Task<GroundingProviderProfile?> GetByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult(_store.FirstOrDefault(g => g.Name == name));

        public Task<GroundingProviderProfile> CreateAsync(GroundingProviderProfile profile, CancellationToken ct = default) { _store.Add(profile); return Task.FromResult(profile); }
        public Task<GroundingProviderProfile> UpdateAsync(GroundingProviderProfile profile, CancellationToken ct = default) { var i = _store.FindIndex(g => g.Name == profile.Name); if (i >= 0) _store[i] = profile; return Task.FromResult(profile); }
        public Task DeleteAsync(string name, CancellationToken ct = default) { _store.RemoveAll(g => g.Name == name); return Task.CompletedTask; }
        public Task RenameAsync(string oldName, string newName, CancellationToken ct = default) { var i = _store.FindIndex(x => x.Name == oldName); if (i >= 0) _store[i] = _store[i] with { Name = newName }; return Task.CompletedTask; }
    }

    private sealed class InMemoryReviewerProfileRepository : IReviewerProfileRepository
    {
        private readonly List<ReviewerProfile> _store = [];
        public Task<IReadOnlyList<ReviewerProfile>> ListAsync(bool includeSystem = true, CancellationToken ct = default) { IReadOnlyList<ReviewerProfile> r = includeSystem ? SystemCrew.ReviewerProfiles.Values.Concat(_store).ToList() : _store; return Task.FromResult(r); }
        public Task<ReviewerProfile?> GetByNameAsync(string name, CancellationToken ct = default) { if (SystemCrew.ReviewerProfiles.TryGetValue(name, out var s)) return Task.FromResult<ReviewerProfile?>(s); return Task.FromResult(_store.FirstOrDefault(r => r.Name == name)); }
        public Task CreateAsync(ReviewerProfile profile, CancellationToken ct = default) { _store.Add(profile); return Task.CompletedTask; }
        public Task UpdateAsync(ReviewerProfile profile, CancellationToken ct = default) { var i = _store.FindIndex(r => r.Name == profile.Name); if (i >= 0) _store[i] = profile; return Task.CompletedTask; }
        public Task DeleteAsync(string name, CancellationToken ct = default) { _store.RemoveAll(r => r.Name == name); return Task.CompletedTask; }
        public Task RenameAsync(string oldName, string newName, CancellationToken ct = default) { var i = _store.FindIndex(x => x.Name == oldName); if (i >= 0) _store[i] = _store[i] with { Name = newName }; return Task.CompletedTask; }
    }

    private sealed class InMemoryExecutorProfileRepository : IExecutorProfileRepository
    {
        private readonly List<ExecutorProfile> _store = [];
        public Task<IReadOnlyList<ExecutorProfile>> ListAsync(bool includeSystem = true, CancellationToken ct = default) { IReadOnlyList<ExecutorProfile> r = includeSystem ? SystemCrew.ExecutorProfiles.Values.Concat(_store).ToList() : _store; return Task.FromResult(r); }
        public Task<ExecutorProfile?> GetByNameAsync(string name, CancellationToken ct = default) { if (SystemCrew.ExecutorProfiles.TryGetValue(name, out var s)) return Task.FromResult<ExecutorProfile?>(s); return Task.FromResult(_store.FirstOrDefault(r => r.Name == name)); }
        public Task CreateAsync(ExecutorProfile profile, CancellationToken ct = default) { _store.Add(profile); return Task.CompletedTask; }
        public Task UpdateAsync(ExecutorProfile profile, CancellationToken ct = default) { var i = _store.FindIndex(r => r.Name == profile.Name); if (i >= 0) _store[i] = profile; return Task.CompletedTask; }
        public Task DeleteAsync(string name, CancellationToken ct = default) { _store.RemoveAll(r => r.Name == name); return Task.CompletedTask; }
        public Task RenameAsync(string oldName, string newName, CancellationToken ct = default) { var i = _store.FindIndex(x => x.Name == oldName); if (i >= 0) _store[i] = _store[i] with { Name = newName }; return Task.CompletedTask; }
    }

    private sealed class InMemoryAdvisorProfileRepository : IAdvisorProfileRepository
    {
        private readonly List<AdvisorProfile> _store = [];
        public Task<IReadOnlyList<AdvisorProfile>> ListAsync(bool includeSystem = true, CancellationToken ct = default) { IReadOnlyList<AdvisorProfile> r = includeSystem ? SystemCrew.AdvisorProfiles.Values.Concat(_store).ToList() : _store; return Task.FromResult(r); }
        public Task<AdvisorProfile?> GetByNameAsync(string name, CancellationToken ct = default) { if (SystemCrew.AdvisorProfiles.TryGetValue(name, out var s)) return Task.FromResult<AdvisorProfile?>(s); return Task.FromResult(_store.FirstOrDefault(a => a.Name == name)); }
        public Task CreateAsync(AdvisorProfile profile, CancellationToken ct = default) { _store.Add(profile); return Task.CompletedTask; }
        public Task UpdateAsync(AdvisorProfile profile, CancellationToken ct = default) { var i = _store.FindIndex(a => a.Name == profile.Name); if (i >= 0) _store[i] = profile; return Task.CompletedTask; }
        public Task DeleteAsync(string name, CancellationToken ct = default) { _store.RemoveAll(a => a.Name == name); return Task.CompletedTask; }
        public Task RenameAsync(string oldName, string newName, CancellationToken ct = default) { var i = _store.FindIndex(x => x.Name == oldName); if (i >= 0) _store[i] = _store[i] with { Name = newName }; return Task.CompletedTask; }
    }

    private sealed class InMemoryCrewTemplateRepository : ICrewTemplateRepository
    {
        private readonly List<CrewTemplate> _store = [];
        public Task<IReadOnlyList<CrewTemplate>> ListAsync(bool includeSystem = true, CancellationToken ct = default) { IReadOnlyList<CrewTemplate> r = includeSystem ? SystemCrew.CrewTemplates.Values.Concat(_store).ToList() : _store; return Task.FromResult(r); }
        public Task<CrewTemplate?> GetByNameAsync(string name, CancellationToken ct = default) { if (SystemCrew.CrewTemplates.TryGetValue(name, out var s)) return Task.FromResult<CrewTemplate?>(s); return Task.FromResult(_store.FirstOrDefault(t => t.Name == name)); }
        public Task CreateAsync(CrewTemplate template, CancellationToken ct = default) { _store.Add(template); return Task.CompletedTask; }
        public Task UpdateAsync(CrewTemplate template, CancellationToken ct = default) { var i = _store.FindIndex(t => t.Name == template.Name); if (i >= 0) _store[i] = template; return Task.CompletedTask; }
        public Task DeleteAsync(string name, CancellationToken ct = default) { _store.RemoveAll(t => t.Name == name); return Task.CompletedTask; }
        public Task RenameAsync(string oldName, string newName, CancellationToken ct = default) { var i = _store.FindIndex(x => x.Name == oldName); if (i >= 0) _store[i] = _store[i] with { Name = newName }; return Task.CompletedTask; }
    }
}
