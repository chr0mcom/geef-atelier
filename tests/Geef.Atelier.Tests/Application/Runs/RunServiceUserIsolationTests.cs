using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Application.Runs;

/// <summary>
/// Unit tests for RunService ownership-check logic.
/// Verifies that non-null requestingUsername triggers isolation,
/// and null requestingUsername bypasses ownership checks (admin mode).
/// </summary>
public sealed class RunServiceUserIsolationTests
{
    // ---------------------------------------------------------------------------
    // GetRunAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetRunAsync_WithMatchingUsername_ReturnsRun()
    {
        var runId = Guid.NewGuid();
        var run   = MakeRun(runId, "alice");
        var repo  = new SingleRunRepository(run, cancellable: false);
        var svc   = MakeService(repo);

        var result = await svc.GetRunAsync(runId, "alice");

        Assert.NotNull(result);
        Assert.Equal(runId, result!.Id);
    }

    [Fact]
    public async Task GetRunAsync_WithMismatchedUsername_ReturnsNull()
    {
        var runId = Guid.NewGuid();
        var run   = MakeRun(runId, "alice");
        var repo  = new SingleRunRepository(run, cancellable: false);
        var svc   = MakeService(repo);

        var result = await svc.GetRunAsync(runId, "bob");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetRunAsync_WithNullUsername_ReturnsRunRegardlessOfOwner()
    {
        var runId = Guid.NewGuid();
        var run   = MakeRun(runId, "alice");
        var repo  = new SingleRunRepository(run, cancellable: false);
        var svc   = MakeService(repo);

        var result = await svc.GetRunAsync(runId, null);

        Assert.NotNull(result);
        Assert.Equal(runId, result!.Id);
    }

    // ---------------------------------------------------------------------------
    // ListRunsAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ListRunsAsync_PassesUsernameToRepository()
    {
        var repo = new RecordingRunRepository();
        var svc  = MakeService(repo);

        await svc.ListRunsAsync(10, null, "charlie");

        Assert.Equal("charlie", repo.LastListUsername);
    }

    [Fact]
    public async Task ListRunsAsync_PassesNullUsernameToRepository_ForAdminMode()
    {
        var repo = new RecordingRunRepository();
        var svc  = MakeService(repo);

        await svc.ListRunsAsync(10, null, null);

        Assert.Null(repo.LastListUsername);
    }

    // ---------------------------------------------------------------------------
    // CancelRunAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CancelRunAsync_ByOwner_ReturnsTrueWhenCancellationSucceeds()
    {
        var runId = Guid.NewGuid();
        var run   = MakeRun(runId, "alice");
        var repo  = new SingleRunRepository(run, cancellable: true);
        var svc   = MakeService(repo);

        var result = await svc.CancelRunAsync(runId, "alice");

        Assert.True(result);
    }

    [Fact]
    public async Task CancelRunAsync_ByNonOwner_ReturnsFalse()
    {
        var runId = Guid.NewGuid();
        var run   = MakeRun(runId, "alice");
        var repo  = new SingleRunRepository(run, cancellable: true);
        var svc   = MakeService(repo);

        var result = await svc.CancelRunAsync(runId, "bob");

        Assert.False(result);
    }

    [Fact]
    public async Task CancelRunAsync_ByAdmin_BypassesOwnerCheck()
    {
        var runId = Guid.NewGuid();
        var run   = MakeRun(runId, "alice");
        var repo  = new SingleRunRepository(run, cancellable: true);
        var svc   = MakeService(repo);

        // null = admin mode, no ownership check
        var result = await svc.CancelRunAsync(runId, null);

        Assert.True(result);
    }

    // ---------------------------------------------------------------------------
    // GetRunDetailsAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetRunDetailsAsync_WithMatchingUsername_ReturnsDetails()
    {
        var runId   = Guid.NewGuid();
        var run     = MakeRun(runId, "alice");
        var details = new RunDetails(run, []);
        var repo    = new SingleRunRepository(run, cancellable: false, details: details);
        var svc     = MakeService(repo);

        var result = await svc.GetRunDetailsAsync(runId, "alice");

        Assert.NotNull(result);
        Assert.Equal(runId, result!.Run.Id);
    }

    [Fact]
    public async Task GetRunDetailsAsync_WithMismatchedUsername_ReturnsNull()
    {
        var runId   = Guid.NewGuid();
        var run     = MakeRun(runId, "alice");
        var details = new RunDetails(run, []);
        var repo    = new SingleRunRepository(run, cancellable: false, details: details);
        var svc     = MakeService(repo);

        var result = await svc.GetRunDetailsAsync(runId, "bob");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetRunDetailsAsync_WithNullUsername_ReturnsAnyRun()
    {
        var runId   = Guid.NewGuid();
        var run     = MakeRun(runId, "alice");
        var details = new RunDetails(run, []);
        var repo    = new SingleRunRepository(run, cancellable: false, details: details);
        var svc     = MakeService(repo);

        var result = await svc.GetRunDetailsAsync(runId, null);

        Assert.NotNull(result);
        Assert.Equal(runId, result!.Run.Id);
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static RunService MakeService(IRunRepository repo) => new RunService(
        new MinimalPersistenceService(),
        repo,
        new MinimalCrewService(),
        new MinimalAdvisorConsultationRepository(),
        new NoOpKnowledgeService(),
        NullLogger<RunService>.Instance);

    private static RunEntity MakeRun(Guid id, string? createdByUser) => new RunEntity
    {
        Id            = id,
        CreatedAt     = DateTimeOffset.UtcNow,
        Status        = RunStatus.Pending,
        BriefingText  = "briefing",
        ConfigJson    = "{}",
        CreatedByUser = createdByUser,
    };

    // ---------------------------------------------------------------------------
    // Fakes / stubs
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Returns a single pre-defined run (or its details) by ID.
    /// <paramref name="cancellable"/> controls whether <see cref="RequestCancellationAsync"/> returns true.
    /// </summary>
    private sealed class SingleRunRepository(RunEntity run, bool cancellable, RunDetails? details = null) : IRunRepository
    {
        public Task<RunEntity?> GetByIdAsync(Guid runId, CancellationToken cancellationToken = default)
            => Task.FromResult(runId == run.Id ? run : (RunEntity?)null);

        public Task<IReadOnlyList<RunEntity>> ListAsync(int limit, RunStatus? statusFilter, string? username, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RunEntity>>([run]);

        public Task<bool> RequestCancellationAsync(Guid runId, CancellationToken cancellationToken = default)
            => Task.FromResult(runId == run.Id && cancellable);

        public Task<RunDetails?> GetDetailsAsync(Guid runId, CancellationToken cancellationToken = default)
            => Task.FromResult(runId == run.Id ? details : null);

        public Task<WelcomeStats> GetWelcomeStatsAsync(string? username, CancellationToken cancellationToken = default)
            => Task.FromResult(new WelcomeStats(0, 0.0, 0.0, 0m, 0, 0m));

        public Task DeleteAsync(Guid runId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>Records the arguments passed to ListAsync so tests can inspect them.</summary>
    private sealed class RecordingRunRepository : IRunRepository
    {
        public string? LastListUsername { get; private set; } = "UNSET";

        public Task<RunEntity?> GetByIdAsync(Guid runId, CancellationToken cancellationToken = default)
            => Task.FromResult<RunEntity?>(null);

        public Task<IReadOnlyList<RunEntity>> ListAsync(int limit, RunStatus? statusFilter, string? username, CancellationToken cancellationToken = default)
        {
            LastListUsername = username;
            return Task.FromResult<IReadOnlyList<RunEntity>>([]);
        }

        public Task<bool> RequestCancellationAsync(Guid runId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<RunDetails?> GetDetailsAsync(Guid runId, CancellationToken cancellationToken = default)
            => Task.FromResult<RunDetails?>(null);

        public Task<WelcomeStats> GetWelcomeStatsAsync(string? username, CancellationToken cancellationToken = default)
            => Task.FromResult(new WelcomeStats(0, 0.0, 0.0, 0m, 0, 0m));

        public Task DeleteAsync(Guid runId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class MinimalPersistenceService : IRunPersistenceService
    {
        public Task<Guid> CreateRunAsync(string briefingText, string configJson, string? createdByUser = null,
            string? crewTemplateName = null, string? crewSnapshotJson = null, RunKind kind = RunKind.Standard,
            Guid? parentCompositionRunId = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Guid.NewGuid());

        public Task UpdateSnapshotAsync(Guid runId, string snapshotJson, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task MarkRunFailedAsync(Guid runId, string errorMessage, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<Guid> CreateResumedRunAsync(string briefingText, string configJson,
            string? createdByUser, string? crewTemplateName, string? crewSnapshotJson,
            Guid parentRunId, string? seedDraftText, CancellationToken cancellationToken = default)
            => Task.FromResult(Guid.NewGuid());
    }

    private sealed class MinimalAdvisorConsultationRepository : IAdvisorConsultationRepository
    {
        public Task<AdvisorConsultation> CreateAsync(AdvisorConsultation consultation, CancellationToken ct)
            => Task.FromResult(consultation);

        public Task<IReadOnlyList<AdvisorConsultation>> GetByRunIdAsync(Guid runId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AdvisorConsultation>>([]);
    }

    private sealed class MinimalCrewService : ICrewService
    {
        public Task<CrewSnapshot> ResolveSnapshotAsync(string? crewTemplateName, CrewSpec? customCrew, CancellationToken cancellationToken = default)
        {
            var snapshot = new CrewSnapshot(
                SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
                TemplateName: crewTemplateName,
                Executor: SystemCrew.DefaultExecutorProfile,
                Reviewers: [SystemCrew.BriefingFidelityProfile],
                EvaluationStrategy: EvaluationStrategy.Parallel,
                ConvergenceOverride: null,
                Advisors: [],
                GroundingProviders: null);
            return Task.FromResult(snapshot);
        }

        public Task<IReadOnlyList<ReviewerProfile>> ListReviewerProfilesAsync(bool includeSystem = true, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReviewerProfile>>([]);
        public Task<ReviewerProfile?> GetReviewerProfileAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult<ReviewerProfile?>(null);
        public Task<ReviewerProfile> CreateCustomReviewerProfileAsync(ReviewerProfile profile, CancellationToken cancellationToken = default) => Task.FromResult(profile);
        public Task<ReviewerProfile> UpdateCustomReviewerProfileAsync(ReviewerProfile profile, CancellationToken cancellationToken = default) => Task.FromResult(profile);
        public Task DeleteCustomReviewerProfileAsync(string name, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> RenameCustomReviewerProfileAsync(string oldName, string newName, CancellationToken cancellationToken = default) => Task.FromResult(newName);
        public Task<IReadOnlyList<ExecutorProfile>> ListExecutorProfilesAsync(bool includeSystem = true, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ExecutorProfile>>([]);
        public Task<ExecutorProfile?> GetExecutorProfileAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult<ExecutorProfile?>(null);
        public Task<ExecutorProfile> CreateCustomExecutorProfileAsync(ExecutorProfile profile, CancellationToken cancellationToken = default) => Task.FromResult(profile);
        public Task<ExecutorProfile> UpdateCustomExecutorProfileAsync(ExecutorProfile profile, CancellationToken cancellationToken = default) => Task.FromResult(profile);
        public Task DeleteCustomExecutorProfileAsync(string name, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> RenameCustomExecutorProfileAsync(string oldName, string newName, CancellationToken cancellationToken = default) => Task.FromResult(newName);
        public Task<IReadOnlyList<AdvisorProfile>> ListAdvisorProfilesAsync(bool includeSystem = true, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AdvisorProfile>>([]);
        public Task<AdvisorProfile?> GetAdvisorProfileAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult<AdvisorProfile?>(null);
        public Task<AdvisorProfile> CreateCustomAdvisorProfileAsync(AdvisorProfile profile, CancellationToken cancellationToken = default) => Task.FromResult(profile);
        public Task<AdvisorProfile> UpdateCustomAdvisorProfileAsync(AdvisorProfile profile, CancellationToken cancellationToken = default) => Task.FromResult(profile);
        public Task DeleteCustomAdvisorProfileAsync(string name, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> RenameCustomAdvisorProfileAsync(string oldName, string newName, CancellationToken cancellationToken = default) => Task.FromResult(newName);
        public Task<IReadOnlyList<GroundingProviderProfile>> ListGroundingProviderProfilesAsync(bool includeSystem = true, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GroundingProviderProfile>>([]);
        public Task<GroundingProviderProfile?> GetGroundingProviderProfileAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult<GroundingProviderProfile?>(null);
        public Task<GroundingProviderProfile> CreateCustomGroundingProviderProfileAsync(GroundingProviderProfile profile, CancellationToken cancellationToken = default) => Task.FromResult(profile);
        public Task<GroundingProviderProfile> UpdateCustomGroundingProviderProfileAsync(GroundingProviderProfile profile, CancellationToken cancellationToken = default) => Task.FromResult(profile);
        public Task DeleteCustomGroundingProviderProfileAsync(string name, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> RenameCustomGroundingProviderProfileAsync(string oldName, string newName, CancellationToken cancellationToken = default) => Task.FromResult(newName);
        public Task<IReadOnlyList<FinalizerProfile>> ListFinalizerProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<FinalizerProfile>>([]);
        public Task<FinalizerProfile?> GetFinalizerProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<FinalizerProfile?>(null);
        public Task<FinalizerProfile> CreateCustomFinalizerProfileAsync(FinalizerProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task<FinalizerProfile> UpdateCustomFinalizerProfileAsync(FinalizerProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task DeleteCustomFinalizerProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomFinalizerProfileAsync(string oldName, string newName, CancellationToken ct = default) => Task.FromResult(newName);
        public Task<IReadOnlyList<CrewTemplate>> ListCrewTemplatesAsync(bool includeSystem = true, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CrewTemplate>>([]);
        public Task<CrewTemplate?> GetCrewTemplateAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult<CrewTemplate?>(null);
        public Task<CrewTemplate> CreateCustomCrewTemplateAsync(CrewTemplate template, CancellationToken cancellationToken = default) => Task.FromResult(template);
        public Task<CrewTemplate> UpdateCustomCrewTemplateAsync(CrewTemplate template, CancellationToken cancellationToken = default) => Task.FromResult(template);
        public Task DeleteCustomCrewTemplateAsync(string name, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> RenameCustomCrewTemplateAsync(string oldName, string newName, CancellationToken cancellationToken = default) => Task.FromResult(newName);
    }
}
