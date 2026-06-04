using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Application.Runs;

/// <summary>
/// Verifies the defensive recursion guard in <see cref="RunService.SubmitRunAsync"/>:
/// a request with <c>Kind == CrewComposition</c> AND <c>AutoCompose == true</c>
/// must throw <see cref="InvalidOperationException"/> immediately.
/// </summary>
public sealed class RunServiceAutoComposeGuardTests
{
    [Fact]
    public async Task SubmitRunAsync_ThrowsOnAutoCompose_WhenKindIsCrewComposition()
    {
        // Arrange
        var svc = MakeService();
        var request = new SubmitRunRequest(
            BriefingText:  "some task",
            ConfigJson:    "{}",
            Kind:          RunKind.CrewComposition,
            AutoCompose:   true);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SubmitRunAsync(request));
    }

    [Fact]
    public async Task SubmitRunAsync_DoesNotThrow_WhenKindIsCrewCompositionButAutoComposeIsFalse()
    {
        // Arrange
        var svc = MakeService();
        var request = new SubmitRunRequest(
            BriefingText: "some task",
            ConfigJson:   "{}",
            Kind:         RunKind.CrewComposition,
            AutoCompose:  false);

        // Act & Assert: should NOT throw
        var id = await svc.SubmitRunAsync(request);
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task SubmitRunAsync_DoesNotThrow_WhenKindIsStandardWithAutoCompose()
    {
        // Arrange: AutoCompose=true + Kind=Standard is the normal valid combination
        var svc = MakeService();
        var request = new SubmitRunRequest(
            BriefingText: "some task",
            ConfigJson:   "{}",
            Kind:         RunKind.Standard,
            AutoCompose:  true);

        // Act & Assert: should redirect to crew-composer, not throw
        var id = await svc.SubmitRunAsync(request);
        Assert.NotEqual(Guid.Empty, id);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static RunService MakeService() => new RunService(
        new NoOpPersistenceService(),
        new NoOpRunRepository(),
        new AutoComposeCrewService(),
        new NoOpAdvisorConsultationRepository(),
        new NoOpKnowledgeService(),
        NullLogger<RunService>.Instance);

    // ── Fakes ─────────────────────────────────────────────────────────────

    private sealed class NoOpPersistenceService : IRunPersistenceService
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

    private sealed class NoOpRunRepository : IRunRepository
    {
        public Task<RunEntity?> GetByIdAsync(Guid runId, CancellationToken cancellationToken = default) => Task.FromResult<RunEntity?>(null);
        public Task<IReadOnlyList<RunEntity>> ListAsync(int limit, RunStatus? statusFilter, string? username, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RunEntity>>([]);
        public Task<bool> RequestCancellationAsync(Guid runId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<RunDetails?> GetDetailsAsync(Guid runId, CancellationToken cancellationToken = default) => Task.FromResult<RunDetails?>(null);
        public Task<WelcomeStats> GetWelcomeStatsAsync(string? username, CancellationToken cancellationToken = default) => Task.FromResult(new WelcomeStats(0, 0.0, 0.0, 0m, 0, 0m));
        public Task DeleteAsync(Guid runId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpAdvisorConsultationRepository : IAdvisorConsultationRepository
    {
        public Task<AdvisorConsultation> CreateAsync(AdvisorConsultation consultation, CancellationToken ct) => Task.FromResult(consultation);
        public Task<IReadOnlyList<AdvisorConsultation>> GetByRunIdAsync(Guid runId, CancellationToken ct) => Task.FromResult<IReadOnlyList<AdvisorConsultation>>([]);
    }

    /// <summary>
    /// A minimal ICrewService that can resolve snapshots for both "crew-composer" and regular templates.
    /// </summary>
    private sealed class AutoComposeCrewService : StubCrewServiceBase
    {
        public override Task<CrewSnapshot> ResolveSnapshotAsync(
            string? crewTemplateName, CrewSpec? customCrew, CancellationToken ct = default)
        {
            var snapshot = new CrewSnapshot(
                SchemaVersion:      CrewSnapshot.CurrentSchemaVersion,
                TemplateName:       crewTemplateName,
                Executor:           SystemCrew.DefaultExecutorProfile,
                Reviewers:          [SystemCrew.BriefingFidelityProfile],
                EvaluationStrategy: EvaluationStrategy.Parallel,
                ConvergenceOverride: null,
                Advisors:           [],
                GroundingProviders: null);
            return Task.FromResult(snapshot);
        }
    }
}
