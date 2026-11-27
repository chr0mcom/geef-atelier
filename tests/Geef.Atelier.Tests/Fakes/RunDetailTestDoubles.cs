using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Persistence;

namespace Geef.Atelier.Tests.Fakes;

/// <summary>
/// Shared <see cref="IRunService"/> stub for RunDetail component tests: serves a single
/// fixed <see cref="RunDetails"/> instance for all read paths.
/// </summary>
internal sealed class StubRunService(RunDetails details, IReadOnlyList<Geef.Atelier.Core.Domain.Warnings.RunWarningEntity>? warnings = null) : IRunService
{
    public Task<IReadOnlyList<Geef.Atelier.Core.Domain.Warnings.RunWarningEntity>> ListRunWarningsAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Geef.Atelier.Core.Domain.Warnings.RunWarningEntity>>(warnings ?? Array.Empty<Geef.Atelier.Core.Domain.Warnings.RunWarningEntity>());

    public Task<IReadOnlyList<RunEntity>> ListRunsAsync(int limit = 20, RunStatus? statusFilter = null,
        string? requestingUsername = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RunEntity>>([details.Run]);

    public Task<RunEntity?> GetRunAsync(Guid id, string? user, CancellationToken ct = default) =>
        Task.FromResult<RunEntity?>(details.Run);

    public Task<bool> CancelRunAsync(Guid id, string? user, CancellationToken ct = default) =>
        Task.FromResult(false);

    public Task<RunDetails?> GetRunDetailsAsync(Guid id, string? user, CancellationToken ct = default) =>
        Task.FromResult<RunDetails?>(details);

    public Task<RunViewModel?> GetRunViewAsync(Guid id, string? user, CancellationToken ct = default) =>
        Task.FromResult<RunViewModel?>(new RunViewModel(details, null, warnings ?? []));

    public Task<Guid> ResumeRunAsync(ResumeOptions options, string? user, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<bool> DeleteRunAsync(Guid runId, string? requestingUsername, CancellationToken ct = default) =>
        Task.FromResult(true);
}
