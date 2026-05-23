using Geef.Atelier.Application.Crew;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Persistence.Crew;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Application.Crew;

/// <summary>
/// Regression tests for listing logic: learning-retriever-default must appear in the provider list.
/// Bug D-055: profile existed in DB with IsSystem=true but was not in SystemCrew code constants,
/// so ListGroundingProviderProfilesAsync silently dropped it.
/// </summary>
public sealed class LearningRetrievalProviderListingTests
{
    [Fact]
    public async Task ListGroundingProviderProfilesAsync_IncludesLearningRetrieverDefault_WhenSystemRequested()
    {
        var svc = BuildService();

        var list = await svc.ListGroundingProviderProfilesAsync(includeSystem: true);

        Assert.Contains(list, p => p.Name == "learning-retriever-default");
    }

    [Fact]
    public async Task ListGroundingProviderProfilesAsync_LearningRetrieverDefault_HasCorrectType()
    {
        var svc = BuildService();

        var list  = await svc.ListGroundingProviderProfilesAsync(includeSystem: true);
        var profile = list.SingleOrDefault(p => p.Name == "learning-retriever-default");

        Assert.NotNull(profile);
        Assert.Equal("learning-retrieval", profile.ProviderType);
        Assert.True(profile.IsSystem);
    }

    [Fact]
    public async Task ListGroundingProviderProfilesAsync_DoesNotIncludeLearningRetriever_WhenSystemExcluded()
    {
        var svc = BuildService();

        var list = await svc.ListGroundingProviderProfilesAsync(includeSystem: false);

        Assert.DoesNotContain(list, p => p.Name == "learning-retriever-default");
    }

    [Fact]
    public async Task ListGroundingProviderProfilesAsync_AllExpectedSystemProfiles_ArePresent()
    {
        var svc = BuildService();

        var list  = await svc.ListGroundingProviderProfilesAsync(includeSystem: true);
        var names = list.Select(p => p.Name).ToHashSet();

        foreach (var name in SystemCrew.GroundingProviderProfiles.Keys)
            Assert.Contains(name, names);
    }

    [Fact]
    public async Task ListGroundingProviderProfilesAsync_OrphanSystemProfileInDb_LogsWarning()
    {
        // Arrange: a DB row marked IsSystem=true that is NOT in SystemCrew (simulates the D-055 bug state)
        var orphan = new GroundingProviderProfile(
            Name: "orphan-system-profile",
            DisplayName: "Orphan",
            Description: "Exists in DB but not in SystemCrew code constants",
            ProviderType: "unknown-type",
            ProviderSettings: [],
            MaxQueriesPerRun: 1,
            IsSystem: true);

        var repo          = new InMemoryGroundingProviderProfileRepository([orphan]);
        var captureLogger = new CapturingLogger<CrewService>();
        var svc           = BuildService(groundingRepo: repo, logger: captureLogger);

        var list = await svc.ListGroundingProviderProfilesAsync(includeSystem: true);

        // The orphan is still dropped (it's not a SystemCrew constant) but a warning is now emitted.
        Assert.DoesNotContain(list, p => p.Name == "orphan-system-profile");
        Assert.Contains(captureLogger.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains("orphan-system-profile"));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ICrewService BuildService(
        InMemoryGroundingProviderProfileRepository? groundingRepo = null,
        ILogger<CrewService>? logger = null)
        => new CrewService(
            new InMemoryReviewerProfileRepository(),
            new InMemoryExecutorProfileRepository(),
            new InMemoryAdvisorProfileRepository(),
            groundingRepo ?? new InMemoryGroundingProviderProfileRepository([]),
            new InMemoryFinalizerProfileRepository(),
            new InMemoryCrewTemplateRepository(),
            logger ?? NullLogger<CrewService>.Instance);

    // ── In-memory fakes ───────────────────────────────────────────────────────

    private sealed class InMemoryGroundingProviderProfileRepository(
        IEnumerable<GroundingProviderProfile> seed) : IGroundingProviderProfileRepository
    {
        private readonly List<GroundingProviderProfile> _store = seed.ToList();

        public Task<IReadOnlyList<GroundingProviderProfile>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GroundingProviderProfile>>(_store);
        public Task<GroundingProviderProfile?> GetByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult(_store.FirstOrDefault(g => g.Name == name));
        public Task<GroundingProviderProfile> CreateAsync(GroundingProviderProfile p, CancellationToken ct = default) { _store.Add(p); return Task.FromResult(p); }
        public Task<GroundingProviderProfile> UpdateAsync(GroundingProviderProfile p, CancellationToken ct = default) { var i = _store.FindIndex(g => g.Name == p.Name); if (i >= 0) _store[i] = p; return Task.FromResult(p); }
        public Task DeleteAsync(string name, CancellationToken ct = default) { _store.RemoveAll(g => g.Name == name); return Task.CompletedTask; }
        public Task RenameAsync(string oldName, string newName, CancellationToken ct = default) { var i = _store.FindIndex(x => x.Name == oldName); if (i >= 0) _store[i] = _store[i] with { Name = newName }; return Task.CompletedTask; }
    }

    private sealed class InMemoryReviewerProfileRepository : IReviewerProfileRepository
    {
        private readonly List<ReviewerProfile> _store = [];
        public Task<IReadOnlyList<ReviewerProfile>> ListAsync(bool includeSystem = true, CancellationToken ct = default) { IReadOnlyList<ReviewerProfile> r = includeSystem ? SystemCrew.ReviewerProfiles.Values.Concat(_store).ToList() : _store; return Task.FromResult(r); }
        public Task<ReviewerProfile?> GetByNameAsync(string name, CancellationToken ct = default) { if (SystemCrew.ReviewerProfiles.TryGetValue(name, out var s)) return Task.FromResult<ReviewerProfile?>(s); return Task.FromResult(_store.FirstOrDefault(r => r.Name == name)); }
        public Task CreateAsync(ReviewerProfile p, CancellationToken ct = default) { _store.Add(p); return Task.CompletedTask; }
        public Task UpdateAsync(ReviewerProfile p, CancellationToken ct = default) { var i = _store.FindIndex(r => r.Name == p.Name); if (i >= 0) _store[i] = p; return Task.CompletedTask; }
        public Task DeleteAsync(string name, CancellationToken ct = default) { _store.RemoveAll(r => r.Name == name); return Task.CompletedTask; }
        public Task RenameAsync(string oldName, string newName, CancellationToken ct = default) { var i = _store.FindIndex(x => x.Name == oldName); if (i >= 0) _store[i] = _store[i] with { Name = newName }; return Task.CompletedTask; }
    }

    private sealed class InMemoryExecutorProfileRepository : IExecutorProfileRepository
    {
        private readonly List<ExecutorProfile> _store = [];
        public Task<IReadOnlyList<ExecutorProfile>> ListAsync(bool includeSystem = true, CancellationToken ct = default) { IReadOnlyList<ExecutorProfile> r = includeSystem ? SystemCrew.ExecutorProfiles.Values.Concat(_store).ToList() : _store; return Task.FromResult(r); }
        public Task<ExecutorProfile?> GetByNameAsync(string name, CancellationToken ct = default) { if (SystemCrew.ExecutorProfiles.TryGetValue(name, out var s)) return Task.FromResult<ExecutorProfile?>(s); return Task.FromResult(_store.FirstOrDefault(r => r.Name == name)); }
        public Task CreateAsync(ExecutorProfile p, CancellationToken ct = default) { _store.Add(p); return Task.CompletedTask; }
        public Task UpdateAsync(ExecutorProfile p, CancellationToken ct = default) { var i = _store.FindIndex(r => r.Name == p.Name); if (i >= 0) _store[i] = p; return Task.CompletedTask; }
        public Task DeleteAsync(string name, CancellationToken ct = default) { _store.RemoveAll(r => r.Name == name); return Task.CompletedTask; }
        public Task RenameAsync(string oldName, string newName, CancellationToken ct = default) { var i = _store.FindIndex(x => x.Name == oldName); if (i >= 0) _store[i] = _store[i] with { Name = newName }; return Task.CompletedTask; }
    }

    private sealed class InMemoryAdvisorProfileRepository : IAdvisorProfileRepository
    {
        private readonly List<AdvisorProfile> _store = [];
        public Task<IReadOnlyList<AdvisorProfile>> ListAsync(bool includeSystem = true, CancellationToken ct = default) { IReadOnlyList<AdvisorProfile> r = includeSystem ? SystemCrew.AdvisorProfiles.Values.Concat(_store).ToList() : _store; return Task.FromResult(r); }
        public Task<AdvisorProfile?> GetByNameAsync(string name, CancellationToken ct = default) { if (SystemCrew.AdvisorProfiles.TryGetValue(name, out var s)) return Task.FromResult<AdvisorProfile?>(s); return Task.FromResult(_store.FirstOrDefault(a => a.Name == name)); }
        public Task CreateAsync(AdvisorProfile p, CancellationToken ct = default) { _store.Add(p); return Task.CompletedTask; }
        public Task UpdateAsync(AdvisorProfile p, CancellationToken ct = default) { var i = _store.FindIndex(a => a.Name == p.Name); if (i >= 0) _store[i] = p; return Task.CompletedTask; }
        public Task DeleteAsync(string name, CancellationToken ct = default) { _store.RemoveAll(a => a.Name == name); return Task.CompletedTask; }
        public Task RenameAsync(string oldName, string newName, CancellationToken ct = default) { var i = _store.FindIndex(x => x.Name == oldName); if (i >= 0) _store[i] = _store[i] with { Name = newName }; return Task.CompletedTask; }
    }

    private sealed class InMemoryFinalizerProfileRepository : IFinalizerProfileRepository
    {
        private readonly List<FinalizerProfile> _store = [];
        public Task<IReadOnlyList<FinalizerProfile>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<FinalizerProfile>>(_store);
        public Task<FinalizerProfile?> GetByNameAsync(string name, CancellationToken ct = default) => Task.FromResult(_store.FirstOrDefault(f => f.Name == name));
        public Task<FinalizerProfile> CreateAsync(FinalizerProfile p, CancellationToken ct = default) { _store.Add(p); return Task.FromResult(p); }
        public Task<FinalizerProfile> UpdateAsync(FinalizerProfile p, CancellationToken ct = default) { var i = _store.FindIndex(f => f.Name == p.Name); if (i >= 0) _store[i] = p; return Task.FromResult(p); }
        public Task DeleteAsync(string name, CancellationToken ct = default) { _store.RemoveAll(f => f.Name == name); return Task.CompletedTask; }
        public Task RenameAsync(string oldName, string newName, CancellationToken ct = default) { var i = _store.FindIndex(x => x.Name == oldName); if (i >= 0) _store[i] = _store[i] with { Name = newName }; return Task.CompletedTask; }
    }

    private sealed class InMemoryCrewTemplateRepository : ICrewTemplateRepository
    {
        private readonly List<CrewTemplate> _store = [];
        public Task<IReadOnlyList<CrewTemplate>> ListAsync(bool includeSystem = true, CancellationToken ct = default) { IReadOnlyList<CrewTemplate> r = includeSystem ? SystemCrew.CrewTemplates.Values.Concat(_store).ToList() : _store; return Task.FromResult(r); }
        public Task<CrewTemplate?> GetByNameAsync(string name, CancellationToken ct = default) { if (SystemCrew.CrewTemplates.TryGetValue(name, out var s)) return Task.FromResult<CrewTemplate?>(s); return Task.FromResult(_store.FirstOrDefault(t => t.Name == name)); }
        public Task CreateAsync(CrewTemplate t, CancellationToken ct = default) { _store.Add(t); return Task.CompletedTask; }
        public Task UpdateAsync(CrewTemplate t, CancellationToken ct = default) { var i = _store.FindIndex(x => x.Name == t.Name); if (i >= 0) _store[i] = t; return Task.CompletedTask; }
        public Task DeleteAsync(string name, CancellationToken ct = default) { _store.RemoveAll(x => x.Name == name); return Task.CompletedTask; }
        public Task RenameAsync(string oldName, string newName, CancellationToken ct = default) { var i = _store.FindIndex(x => x.Name == oldName); if (i >= 0) _store[i] = _store[i] with { Name = newName }; return Task.CompletedTask; }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
