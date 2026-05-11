using Geef.Atelier.Core.Domain;

namespace Geef.Atelier.Core.Persistence;

/// <summary>Read and cancellation-request operations for Runs.</summary>
public interface IRunRepository
{
    /// <summary>Returns the Run with the given ID, or null if not found.</summary>
    Task<RunEntity?> GetByIdAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>Returns the most recent runs, optionally filtered by status, ordered by CreatedAt descending.</summary>
    Task<IReadOnlyList<RunEntity>> ListAsync(int limit, RunStatus? statusFilter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically sets CancellationRequested=true if the run is Pending or Running and not yet cancelled.
    /// Returns true if the flag was set, false if the run was already terminal or already cancelled.
    /// </summary>
    Task<bool> RequestCancellationAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>Returns the run with all its iterations and findings, or null if the run does not exist.</summary>
    Task<RunDetails?> GetDetailsAsync(Guid runId, CancellationToken cancellationToken = default);
}
