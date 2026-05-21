using Geef.Atelier.Core.Domain;

namespace Geef.Atelier.Core.Persistence;

/// <summary>Read and cancellation-request operations for Runs.</summary>
public interface IRunRepository
{
    /// <summary>Returns the Run with the given ID, or null if not found.</summary>
    Task<RunEntity?> GetByIdAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the most recent runs, optionally filtered by status and username, ordered by CreatedAt descending.
    /// Pass <c>null</c> for <paramref name="username"/> to return runs for all users (Admin-mode).
    /// </summary>
    Task<IReadOnlyList<RunEntity>> ListAsync(int limit, RunStatus? statusFilter, string? username, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically sets CancellationRequested=true if the run is Pending or Running and not yet cancelled.
    /// Returns true if the flag was set, false if the run was already terminal or already cancelled.
    /// </summary>
    Task<bool> RequestCancellationAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>Returns the run with all its iterations and findings, or null if the run does not exist.</summary>
    Task<RunDetails?> GetDetailsAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns aggregated statistics for the current calendar month.
    /// Pass <c>null</c> for <paramref name="username"/> for system-wide stats (Admin-mode);
    /// pass a username to scope run-level statistics to that user only.
    /// Studio-analysis stats are never user-scoped.
    /// </summary>
    Task<WelcomeStats> GetWelcomeStatsAsync(string? username, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes the run with the given ID and all associated data (iterations, findings, events, etc.).
    /// No-op if the run does not exist.
    /// </summary>
    Task DeleteAsync(Guid runId, CancellationToken cancellationToken = default);
}
