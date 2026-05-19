using System.Text.Json;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Application.Runs;

public sealed class RunServiceResumeTests
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // ── Happy path: seed mode ─────────────────────────────────────────────

    [Fact]
    public async Task ResumeRunAsync_SeedMode_PassesSeedDraftFromLastIteration()
    {
        var parentId  = Guid.NewGuid();
        var iteration = MakeIteration(parentId, 1, "draft from iteration 1");
        var parent    = MakeRun(parentId, RunStatus.Aborted, "alice");
        var capturing = new CapturingPersistenceService();
        var repo      = new SingleDetailsRepository(parent, [iteration]);
        var svc       = MakeService(capturing, repo);

        await svc.ResumeRunAsync(new ResumeOptions(parentId, UseSeedDraft: true, MaxIterationsOverride: null), "alice");

        Assert.Equal("draft from iteration 1", capturing.LastSeedDraftText);
        Assert.Equal(parentId, capturing.LastParentRunId);
    }

    [Fact]
    public async Task ResumeRunAsync_SeedMode_PrefersHighestIterationNumber()
    {
        var parentId  = Guid.NewGuid();
        var iter1     = MakeIteration(parentId, 1, "draft 1");
        var iter2     = MakeIteration(parentId, 2, "draft 2");
        var parent    = MakeRun(parentId, RunStatus.Aborted, "alice");
        var capturing = new CapturingPersistenceService();
        var repo      = new SingleDetailsRepository(parent, [iter1, iter2]);
        var svc       = MakeService(capturing, repo);

        await svc.ResumeRunAsync(new ResumeOptions(parentId, UseSeedDraft: true, MaxIterationsOverride: null), "alice");

        Assert.Equal("draft 2", capturing.LastSeedDraftText);
    }

    [Fact]
    public async Task ResumeRunAsync_SeedMode_NoIterations_SeedDraftIsNull()
    {
        var parentId  = Guid.NewGuid();
        var parent    = MakeRun(parentId, RunStatus.Aborted, "alice");
        var capturing = new CapturingPersistenceService();
        var repo      = new SingleDetailsRepository(parent, []);
        var svc       = MakeService(capturing, repo);

        await svc.ResumeRunAsync(new ResumeOptions(parentId, UseSeedDraft: true, MaxIterationsOverride: null), "alice");

        Assert.Null(capturing.LastSeedDraftText);
    }

    // ── Happy path: clean retry ───────────────────────────────────────────

    [Fact]
    public async Task ResumeRunAsync_CleanRetry_SeedDraftIsNull()
    {
        var parentId  = Guid.NewGuid();
        var iteration = MakeIteration(parentId, 1, "draft");
        var parent    = MakeRun(parentId, RunStatus.Failed, "alice");
        var capturing = new CapturingPersistenceService();
        var repo      = new SingleDetailsRepository(parent, [iteration]);
        var svc       = MakeService(capturing, repo);

        await svc.ResumeRunAsync(new ResumeOptions(parentId, UseSeedDraft: false, MaxIterationsOverride: null), "alice");

        Assert.Null(capturing.LastSeedDraftText);
        Assert.Equal(parentId, capturing.LastParentRunId);
    }

    // ── MaxIterationsOverride ─────────────────────────────────────────────

    [Fact]
    public async Task ResumeRunAsync_WithMaxIterationsOverride_PatchesConvergenceOverrideInSnapshot()
    {
        var parentId = Guid.NewGuid();
        var snapshot = new CrewSnapshot(
            SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
            TemplateName: null,
            Executor: SystemCrew.DefaultExecutorProfile,
            Reviewers: [SystemCrew.BriefingFidelityProfile],
            EvaluationStrategy: EvaluationStrategy.Parallel,
            ConvergenceOverride: null,
            Advisors: []);
        var snapshotJson = JsonSerializer.Serialize(snapshot, JsonOpts);
        var parent    = MakeRunWithSnapshot(parentId, RunStatus.Aborted, "alice", snapshotJson);
        var capturing = new CapturingPersistenceService();
        var repo      = new SingleDetailsRepository(parent, []);
        var svc       = MakeService(capturing, repo);

        await svc.ResumeRunAsync(new ResumeOptions(parentId, UseSeedDraft: false, MaxIterationsOverride: 8), "alice");

        var newSnapshot = CrewSnapshot.Deserialize(capturing.LastSnapshotJson);
        Assert.NotNull(newSnapshot);
        Assert.Equal(8, newSnapshot!.ConvergenceOverride!.MaxIterations);
    }

    // ── Guards ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(RunStatus.Pending)]
    [InlineData(RunStatus.Running)]
    [InlineData(RunStatus.Completed)]
    public async Task ResumeRunAsync_NonResumableStatus_Throws(RunStatus status)
    {
        var parentId = Guid.NewGuid();
        var parent   = MakeRun(parentId, status, "alice");
        var repo     = new SingleDetailsRepository(parent, []);
        var svc      = MakeService(new CapturingPersistenceService(), repo);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ResumeRunAsync(new ResumeOptions(parentId, UseSeedDraft: false, MaxIterationsOverride: null), "alice"));
    }

    [Fact]
    public async Task ResumeRunAsync_WrongOwner_Throws()
    {
        var parentId = Guid.NewGuid();
        var parent   = MakeRun(parentId, RunStatus.Aborted, "alice");
        var repo     = new SingleDetailsRepository(parent, []);
        var svc      = MakeService(new CapturingPersistenceService(), repo);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ResumeRunAsync(new ResumeOptions(parentId, UseSeedDraft: false, MaxIterationsOverride: null), "bob"));
    }

    [Fact]
    public async Task ResumeRunAsync_NullUsername_BypassesOwnerCheck()
    {
        var parentId  = Guid.NewGuid();
        var parent    = MakeRun(parentId, RunStatus.Aborted, "alice");
        var capturing = new CapturingPersistenceService();
        var repo      = new SingleDetailsRepository(parent, []);
        var svc       = MakeService(capturing, repo);

        var result = await svc.ResumeRunAsync(new ResumeOptions(parentId, UseSeedDraft: false, MaxIterationsOverride: null), null);

        Assert.NotEqual(Guid.Empty, result);
    }

    [Fact]
    public async Task ResumeRunAsync_NotFound_Throws()
    {
        var svc = MakeService(new CapturingPersistenceService(), new EmptyRepository());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ResumeRunAsync(new ResumeOptions(Guid.NewGuid(), UseSeedDraft: false, MaxIterationsOverride: null), null));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static RunService MakeService(IRunPersistenceService persistence, IRunRepository repo) =>
        new RunService(
            persistence,
            repo,
            new NoOpCrewService(),
            new NoOpAdvisorConsultationRepository(),
            new NoOpKnowledgeService(),
            NullLogger<RunService>.Instance);

    private static RunEntity MakeRun(Guid id, RunStatus status, string? createdByUser) =>
        new RunEntity
        {
            Id = id, CreatedAt = DateTimeOffset.UtcNow, Status = status,
            BriefingText = "briefing", ConfigJson = "{}", CreatedByUser = createdByUser,
        };

    private static RunEntity MakeRunWithSnapshot(Guid id, RunStatus status, string? createdByUser, string? snapshotJson) =>
        new RunEntity
        {
            Id = id, CreatedAt = DateTimeOffset.UtcNow, Status = status,
            BriefingText = "briefing", ConfigJson = "{}", CreatedByUser = createdByUser,
            CrewSnapshot = snapshotJson,
        };

    private static IterationWithFindings MakeIteration(Guid runId, int number, string text) =>
        new IterationWithFindings(
            new IterationEntity
            {
                Id = Guid.NewGuid(), RunId = runId, IterationNumber = number,
                ArtifactText = text, CreatedAt = DateTimeOffset.UtcNow,
            },
            []);

    // ── Fakes ─────────────────────────────────────────────────────────────

    private sealed class CapturingPersistenceService : IRunPersistenceService
    {
        public Guid?   LastParentRunId   { get; private set; }
        public string? LastSeedDraftText { get; private set; }
        public string? LastSnapshotJson  { get; private set; }

        public Task<Guid> CreateRunAsync(string briefingText, string configJson,
            string? createdByUser = null, string? crewTemplateName = null,
            string? crewSnapshotJson = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Guid.NewGuid());

        public Task<Guid> CreateResumedRunAsync(string briefingText, string configJson,
            string? createdByUser, string? crewTemplateName, string? crewSnapshotJson,
            Guid parentRunId, string? seedDraftText, CancellationToken cancellationToken = default)
        {
            LastParentRunId   = parentRunId;
            LastSeedDraftText = seedDraftText;
            LastSnapshotJson  = crewSnapshotJson;
            return Task.FromResult(Guid.NewGuid());
        }

        public Task UpdateSnapshotAsync(Guid runId, string snapshotJson, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task MarkRunFailedAsync(Guid runId, string errorMessage, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class SingleDetailsRepository(RunEntity run, IReadOnlyList<IterationWithFindings> iterations) : IRunRepository
    {
        public Task<RunEntity?> GetByIdAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult(runId == run.Id ? run : (RunEntity?)null);

        public Task<RunDetails?> GetDetailsAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult(runId == run.Id ? new RunDetails(run, iterations) : (RunDetails?)null);

        public Task<IReadOnlyList<RunEntity>> ListAsync(int limit, RunStatus? statusFilter, string? username, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RunEntity>>([run]);

        public Task<bool> RequestCancellationAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<WelcomeStats> GetWelcomeStatsAsync(string? username, CancellationToken ct = default)
            => Task.FromResult(new WelcomeStats(0, 0.0, 0.0, 0m, 0, 0m));
    }

    private sealed class EmptyRepository : IRunRepository
    {
        public Task<RunEntity?> GetByIdAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult<RunEntity?>(null);

        public Task<RunDetails?> GetDetailsAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult<RunDetails?>(null);

        public Task<IReadOnlyList<RunEntity>> ListAsync(int limit, RunStatus? statusFilter, string? username, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RunEntity>>([]);

        public Task<bool> RequestCancellationAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<WelcomeStats> GetWelcomeStatsAsync(string? username, CancellationToken ct = default)
            => Task.FromResult(new WelcomeStats(0, 0.0, 0.0, 0m, 0, 0m));
    }

    private sealed class NoOpAdvisorConsultationRepository : IAdvisorConsultationRepository
    {
        public Task<AdvisorConsultation> CreateAsync(AdvisorConsultation c, CancellationToken ct) => Task.FromResult(c);
        public Task<IReadOnlyList<AdvisorConsultation>> GetByRunIdAsync(Guid runId, CancellationToken ct) => Task.FromResult<IReadOnlyList<AdvisorConsultation>>([]);
    }

    private sealed class NoOpCrewService : ICrewService
    {
        public Task<CrewSnapshot> ResolveSnapshotAsync(string? crewTemplateName, CrewSpec? customCrew, CancellationToken ct = default)
        {
            var s = new CrewSnapshot(CrewSnapshot.CurrentSchemaVersion, null, SystemCrew.DefaultExecutorProfile,
                [SystemCrew.BriefingFidelityProfile], EvaluationStrategy.Parallel, null, []);
            return Task.FromResult(s);
        }
        public Task<IReadOnlyList<ReviewerProfile>> ListReviewerProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ReviewerProfile>>([]);
        public Task<ReviewerProfile?> GetReviewerProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<ReviewerProfile?>(null);
        public Task<ReviewerProfile> CreateCustomReviewerProfileAsync(ReviewerProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<ReviewerProfile> UpdateCustomReviewerProfileAsync(ReviewerProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomReviewerProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomReviewerProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<ExecutorProfile>> ListExecutorProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ExecutorProfile>>([]);
        public Task<ExecutorProfile?> GetExecutorProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<ExecutorProfile?>(null);
        public Task<ExecutorProfile> CreateCustomExecutorProfileAsync(ExecutorProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<ExecutorProfile> UpdateCustomExecutorProfileAsync(ExecutorProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomExecutorProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomExecutorProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<AdvisorProfile>> ListAdvisorProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AdvisorProfile>>([]);
        public Task<AdvisorProfile?> GetAdvisorProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<AdvisorProfile?>(null);
        public Task<AdvisorProfile> CreateCustomAdvisorProfileAsync(AdvisorProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<AdvisorProfile> UpdateCustomAdvisorProfileAsync(AdvisorProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomAdvisorProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomAdvisorProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<GroundingProviderProfile>> ListGroundingProviderProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GroundingProviderProfile>>([]);
        public Task<GroundingProviderProfile?> GetGroundingProviderProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<GroundingProviderProfile?>(null);
        public Task<GroundingProviderProfile> CreateCustomGroundingProviderProfileAsync(GroundingProviderProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<GroundingProviderProfile> UpdateCustomGroundingProviderProfileAsync(GroundingProviderProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomGroundingProviderProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomGroundingProviderProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<FinalizerProfile>> ListFinalizerProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<FinalizerProfile>>([]);
        public Task<FinalizerProfile?> GetFinalizerProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<FinalizerProfile?>(null);
        public Task<FinalizerProfile> CreateCustomFinalizerProfileAsync(FinalizerProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<FinalizerProfile> UpdateCustomFinalizerProfileAsync(FinalizerProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomFinalizerProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomFinalizerProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<CrewTemplate>> ListCrewTemplatesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CrewTemplate>>([]);
        public Task<CrewTemplate?> GetCrewTemplateAsync(string name, CancellationToken ct = default) => Task.FromResult<CrewTemplate?>(null);
        public Task<CrewTemplate> CreateCustomCrewTemplateAsync(CrewTemplate t, CancellationToken ct = default) => Task.FromResult(t);
        public Task<CrewTemplate> UpdateCustomCrewTemplateAsync(CrewTemplate t, CancellationToken ct = default) => Task.FromResult(t);
        public Task DeleteCustomCrewTemplateAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomCrewTemplateAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
    }
}
