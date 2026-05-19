using Geef.Atelier.Core.Domain;

namespace Geef.Atelier.Application.Runs;

/// <summary>Application-service contract for submitting, querying, listing, and cancelling pipeline runs.</summary>
public interface IRunService
{
    /// <summary>Submits a new run request for processing.</summary>
    Task<Guid> SubmitRunAsync(SubmitRunRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the run with the given ID, or null if not found.
    /// When <paramref name="requestingUsername"/> is non-null, returns null if the run belongs to a different user.
    /// Pass null to bypass the ownership check (admin mode).
    /// </summary>
    Task<RunEntity?> GetRunAsync(
        Guid runId,
        string? requestingUsername,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the most recent runs, optionally filtered by status, ordered by creation time descending.
    /// When <paramref name="requestingUsername"/> is non-null, only runs owned by that user are returned.
    /// Pass null to return runs for all users (admin mode).
    /// </summary>
    Task<IReadOnlyList<RunEntity>> ListRunsAsync(
        int limit = 20,
        RunStatus? statusFilter = null,
        string? requestingUsername = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests cancellation of the run. Returns true if the cancellation flag was set,
    /// false if the run is already in a terminal state, was already cancelled, or does not belong to
    /// <paramref name="requestingUsername"/> (when non-null).
    /// Pass null to bypass the ownership check (admin mode).
    /// </summary>
    Task<bool> CancelRunAsync(
        Guid runId,
        string? requestingUsername,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the run with all its iterations and findings, or null if not found.
    /// When <paramref name="requestingUsername"/> is non-null, returns null if the run belongs to a different user.
    /// Pass null to bypass the ownership check (admin mode).
    /// </summary>
    Task<RunDetails?> GetRunDetailsAsync(
        Guid runId,
        string? requestingUsername,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a <see cref="RunWithGroundingViewModel"/> that groups advisor consultations by trigger type,
    /// or null if the run does not exist or belongs to a different user (when <paramref name="requestingUsername"/> is non-null).
    /// Pass null to bypass the ownership check (admin mode).
    /// </summary>
    Task<RunWithGroundingViewModel?> GetRunWithGroundingAsync(
        Guid runId,
        string? requestingUsername,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns aggregated welcome-page statistics.
    /// When <paramref name="requestingUsername"/> is non-null, stats are scoped to that user's runs.
    /// Pass null for system-wide stats (admin mode).
    /// </summary>
    Task<WelcomeStats> GetWelcomeStatsAsync(
        string? requestingUsername,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new run that resumes a previously aborted or failed run.
    /// Returns the ID of the newly created run.
    /// Throws <see cref="InvalidOperationException"/> if the parent run does not exist,
    /// does not belong to <paramref name="requestingUsername"/> (when non-null),
    /// or is not in a resumable state (Aborted or Failed).
    /// </summary>
    Task<Guid> ResumeRunAsync(
        ResumeOptions options,
        string? requestingUsername,
        CancellationToken cancellationToken = default);
}
