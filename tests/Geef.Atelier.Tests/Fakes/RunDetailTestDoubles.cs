using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Persistence;

namespace Geef.Atelier.Tests.Fakes;

/// <summary>
/// Shared <see cref="IRunService"/> stub for RunDetail component tests: serves a single
/// fixed <see cref="RunDetails"/> instance for all read paths.
/// </summary>
internal sealed class StubRunService(RunDetails details) : IRunService
{
    public Task<IReadOnlyList<RunEntity>> ListRunsAsync(int limit = 20, RunStatus? statusFilter = null,
        string? requestingUsername = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RunEntity>>([details.Run]);

    public Task<Guid> SubmitRunAsync(SubmitRunRequest req, CancellationToken ct = default) =>
        Task.FromResult(Guid.NewGuid());

    public Task<RunEntity?> GetRunAsync(Guid id, string? user, CancellationToken ct = default) =>
        Task.FromResult<RunEntity?>(details.Run);

    public Task<bool> CancelRunAsync(Guid id, string? user, CancellationToken ct = default) =>
        Task.FromResult(false);

    public Task<RunDetails?> GetRunDetailsAsync(Guid id, string? user, CancellationToken ct = default) =>
        Task.FromResult<RunDetails?>(details);

    public Task<RunWithGroundingViewModel?> GetRunWithGroundingAsync(Guid id, string? user, CancellationToken ct = default) =>
        Task.FromResult<RunWithGroundingViewModel?>(new RunWithGroundingViewModel(
            Details: details,
            Snapshot: null,
            GroundedBrief: details.Run.BriefingText,
            GroundingAdvisors: [],
            RecoveryAdvisors: [],
            AdvisorsByIteration: Enumerable.Empty<AdvisorConsultation>().ToLookup(x => x.IterationNumber),
            GroundingConsultations: Array.Empty<GroundingConsultation>(),
            ToolInvocations: []));

    public Task<WelcomeStats> GetWelcomeStatsAsync(string? user, CancellationToken ct = default) =>
        Task.FromResult(new WelcomeStats(0, 0, 0, 0, 0, 0));

    public Task<Guid> ResumeRunAsync(ResumeOptions options, string? user, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<bool> DeleteRunAsync(Guid runId, string? requestingUsername, CancellationToken ct = default) =>
        Task.FromResult(true);
}

/// <summary>Shared <see cref="IRunArtifactRepository"/> stub serving a fixed artifact list.</summary>
internal sealed class StubArtifactRepository(IReadOnlyList<RunArtifact> artifacts)
    : IRunArtifactRepository
{
    public Task<IReadOnlyList<RunArtifact>> ListByRunAsync(Guid runId, CancellationToken ct) =>
        Task.FromResult(artifacts);

    public Task<RunArtifact?> GetByIdAsync(Guid artifactId, CancellationToken ct) =>
        Task.FromResult(artifacts.FirstOrDefault(a => a.Id == artifactId));

    public Task<RunArtifact> CreateAsync(RunArtifact a, CancellationToken ct) =>
        Task.FromResult(a);

    public Task DeleteByRunAsync(Guid runId, CancellationToken ct) =>
        Task.CompletedTask;
}
