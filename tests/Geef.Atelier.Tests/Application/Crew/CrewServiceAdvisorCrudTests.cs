using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Persistence.Crew;

namespace Geef.Atelier.Tests.Application.Crew;

public sealed class CrewServiceAdvisorCrudTests
{
    [Fact]
    public async Task CreateCustomAdvisorProfileAsync_AddsCustomPrefix()
    {
        var repo = new InMemoryAdvisorProfileRepository();
        var svc  = BuildService(advisorRepo: repo);

        var result = await svc.CreateCustomAdvisorProfileAsync(BuildAdvisorProfile("my-advisor"));

        Assert.Equal("custom-my-advisor", result.Name);
        Assert.False(result.IsSystem);
    }

    [Fact]
    public async Task CreateCustomAdvisorProfileAsync_ThrowsOnSystemName()
    {
        var svc = BuildService();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CreateCustomAdvisorProfileAsync(SystemCrew.BriefingClarifierProfile));
    }

    [Fact]
    public async Task DeleteCustomAdvisorProfileAsync_ThrowsOnSystemName()
    {
        var svc = BuildService();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.DeleteCustomAdvisorProfileAsync(SystemCrew.BriefingClarifierProfile.Name));
    }

    [Fact]
    public async Task UpdateCustomAdvisorProfileAsync_ThrowsOnSystemName()
    {
        var svc = BuildService();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.UpdateCustomAdvisorProfileAsync(SystemCrew.DevilsAdvocateProfile));
    }

    [Fact]
    public async Task ListAdvisorProfilesAsync_IncludesSystemProfiles_WhenRequested()
    {
        var repo = new InMemoryAdvisorProfileRepository();
        var svc  = BuildService(advisorRepo: repo);

        var all = await svc.ListAdvisorProfilesAsync(includeSystem: true);

        Assert.Contains(all, p => p.Name == SystemCrew.BriefingClarifierProfile.Name);
        Assert.Contains(all, p => p.Name == SystemCrew.DevilsAdvocateProfile.Name);
    }

    // --- Helpers ---

    private static ICrewService BuildService(
        InMemoryReviewerProfileRepository? reviewerRepo = null,
        InMemoryExecutorProfileRepository?          executorRepo  = null,
        InMemoryAdvisorProfileRepository?           advisorRepo   = null,
        InMemoryGroundingProviderProfileRepository? groundingRepo = null,
        InMemoryCrewTemplateRepository?             templateRepo  = null)
        => new CrewService(
            reviewerRepo  ?? new InMemoryReviewerProfileRepository(),
            executorRepo  ?? new InMemoryExecutorProfileRepository(),
            advisorRepo   ?? new InMemoryAdvisorProfileRepository(),
            groundingRepo ?? new InMemoryGroundingProviderProfileRepository(),
            new InMemoryFinalizerProfileRepository(),
            templateRepo  ?? new InMemoryCrewTemplateRepository());

    private static AdvisorProfile BuildAdvisorProfile(string name) => new(
        Name: name,
        DisplayName: name,
        Description: "desc",
        SystemPrompt: "prompt",
        Provider: "openrouter",
        Model: "model",
        MaxTokens: null,
        Mode: AdvisorMode.Strategic,
        Trigger: AdvisorTrigger.BeforeFirstExecution,
        IsSystem: false);

    // --- In-memory repo stubs (reused from CrewServiceAutoPrexfixTests pattern) ---

    private sealed class InMemoryReviewerProfileRepository : IReviewerProfileRepository
    {
        private readonly List<ReviewerProfile> _store = [];

        public Task<IReadOnlyList<ReviewerProfile>> ListAsync(bool includeSystem = true, CancellationToken ct = default)
        {
            IReadOnlyList<ReviewerProfile> result = includeSystem
                ? SystemCrew.ReviewerProfiles.Values.Concat(_store).ToList()
                : _store;
            return Task.FromResult(result);
        }

        public Task<ReviewerProfile?> GetByNameAsync(string name, CancellationToken ct = default)
        {
            if (SystemCrew.ReviewerProfiles.TryGetValue(name, out var sys)) return Task.FromResult<ReviewerProfile?>(sys);
            return Task.FromResult(_store.FirstOrDefault(r => r.Name == name));
        }

        public Task CreateAsync(ReviewerProfile profile, CancellationToken ct = default) { _store.Add(profile); return Task.CompletedTask; }
        public Task UpdateAsync(ReviewerProfile profile, CancellationToken ct = default) { var i = _store.FindIndex(r => r.Name == profile.Name); if (i >= 0) _store[i] = profile; return Task.CompletedTask; }
        public Task DeleteAsync(string name, CancellationToken ct = default) { _store.RemoveAll(r => r.Name == name); return Task.CompletedTask; }
        public Task RenameAsync(string oldName, string newName, CancellationToken ct = default) { var i = _store.FindIndex(x => x.Name == oldName); if (i >= 0) _store[i] = _store[i] with { Name = newName }; return Task.CompletedTask; }
    }

    private sealed class InMemoryExecutorProfileRepository : IExecutorProfileRepository
    {
        private readonly List<ExecutorProfile> _store = [];

        public Task<IReadOnlyList<ExecutorProfile>> ListAsync(bool includeSystem = true, CancellationToken ct = default)
        {
            IReadOnlyList<ExecutorProfile> result = includeSystem
                ? SystemCrew.ExecutorProfiles.Values.Concat(_store).ToList()
                : _store;
            return Task.FromResult(result);
        }

        public Task<ExecutorProfile?> GetByNameAsync(string name, CancellationToken ct = default)
        {
            if (SystemCrew.ExecutorProfiles.TryGetValue(name, out var sys)) return Task.FromResult<ExecutorProfile?>(sys);
            return Task.FromResult(_store.FirstOrDefault(r => r.Name == name));
        }

        public Task CreateAsync(ExecutorProfile profile, CancellationToken ct = default) { _store.Add(profile); return Task.CompletedTask; }
        public Task UpdateAsync(ExecutorProfile profile, CancellationToken ct = default) { var i = _store.FindIndex(r => r.Name == profile.Name); if (i >= 0) _store[i] = profile; return Task.CompletedTask; }
        public Task DeleteAsync(string name, CancellationToken ct = default) { _store.RemoveAll(r => r.Name == name); return Task.CompletedTask; }
        public Task RenameAsync(string oldName, string newName, CancellationToken ct = default) { var i = _store.FindIndex(x => x.Name == oldName); if (i >= 0) _store[i] = _store[i] with { Name = newName }; return Task.CompletedTask; }
    }

    private sealed class InMemoryCrewTemplateRepository : ICrewTemplateRepository
    {
        private readonly List<CrewTemplate> _store = [];

        public Task<IReadOnlyList<CrewTemplate>> ListAsync(bool includeSystem = true, CancellationToken ct = default)
        {
            IReadOnlyList<CrewTemplate> result = includeSystem
                ? SystemCrew.CrewTemplates.Values.Concat(_store).ToList()
                : _store;
            return Task.FromResult(result);
        }

        public Task<CrewTemplate?> GetByNameAsync(string name, CancellationToken ct = default)
        {
            if (SystemCrew.CrewTemplates.TryGetValue(name, out var sys)) return Task.FromResult<CrewTemplate?>(sys);
            return Task.FromResult(_store.FirstOrDefault(t => t.Name == name));
        }

        public Task CreateAsync(CrewTemplate template, CancellationToken ct = default) { _store.Add(template); return Task.CompletedTask; }
        public Task UpdateAsync(CrewTemplate template, CancellationToken ct = default) { var i = _store.FindIndex(t => t.Name == template.Name); if (i >= 0) _store[i] = template; return Task.CompletedTask; }
        public Task DeleteAsync(string name, CancellationToken ct = default) { _store.RemoveAll(t => t.Name == name); return Task.CompletedTask; }
        public Task RenameAsync(string oldName, string newName, CancellationToken ct = default) { var i = _store.FindIndex(x => x.Name == oldName); if (i >= 0) _store[i] = _store[i] with { Name = newName }; return Task.CompletedTask; }
    }

    private sealed class InMemoryAdvisorProfileRepository : IAdvisorProfileRepository
    {
        private readonly List<AdvisorProfile> _store = [];

        public Task<IReadOnlyList<AdvisorProfile>> ListAsync(bool includeSystem = true, CancellationToken ct = default)
        {
            IReadOnlyList<AdvisorProfile> result = includeSystem
                ? SystemCrew.AdvisorProfiles.Values.Concat(_store).ToList()
                : _store;
            return Task.FromResult(result);
        }

        public Task<AdvisorProfile?> GetByNameAsync(string name, CancellationToken ct = default)
        {
            if (SystemCrew.AdvisorProfiles.TryGetValue(name, out var sys)) return Task.FromResult<AdvisorProfile?>(sys);
            return Task.FromResult(_store.FirstOrDefault(a => a.Name == name));
        }

        public Task CreateAsync(AdvisorProfile profile, CancellationToken ct = default) { _store.Add(profile); return Task.CompletedTask; }
        public Task UpdateAsync(AdvisorProfile profile, CancellationToken ct = default) { var i = _store.FindIndex(a => a.Name == profile.Name); if (i >= 0) _store[i] = profile; return Task.CompletedTask; }
        public Task DeleteAsync(string name, CancellationToken ct = default) { _store.RemoveAll(a => a.Name == name); return Task.CompletedTask; }
        public Task RenameAsync(string oldName, string newName, CancellationToken ct = default) { var i = _store.FindIndex(x => x.Name == oldName); if (i >= 0) _store[i] = _store[i] with { Name = newName }; return Task.CompletedTask; }
    }

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
}
