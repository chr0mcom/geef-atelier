using Geef.Atelier.Core.Domain;

namespace Geef.Atelier.Application.Runs;

/// <summary>Application-service contract for querying, listing, cancelling, resuming and deleting pipeline runs.</summary>
public interface IRunService
{
    /// <summary>
    /// Returns the warnings recorded for a run (Step47), in the order they were recorded. Returns an empty list
    /// when the run does not exist or <paramref name="requestingUsername"/> is set and does not own it.
    /// Pass null to bypass the ownership check (admin mode).
    /// </summary>
    Task<IReadOnlyList<Core.Domain.Warnings.RunWarningEntity>> ListRunWarningsAsync(
        Guid runId, string? requestingUsername, CancellationToken cancellationToken = default);

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
    /// Creates a new run that resumes a previously aborted or failed run; it joins the order of the parent as its next
    /// version. Returns the ID of the newly created run.
    /// Throws <see cref="InvalidOperationException"/> if the parent run does not exist,
    /// does not belong to <paramref name="requestingUsername"/> (when non-null),
    /// or is not in a resumable state (Aborted or Failed).
    /// Throws <see cref="RunNotResumableException"/> (with a message for the user) when the parent run has no usable
    /// crew snapshot, for example because its attachments could not be prepared; no new run is created then.
    /// A parent run that changed an earlier version of its order (<see cref="RunEntity.RevisionHint"/> set) is resumed as a change
    /// itself: the new run makes the same change to the version shown now (<see cref="Core.Domain.Orders.OrderStateResolver.SelectShownRun"/>), seeded
    /// with its text and with the briefing composed from its briefing (<see cref="Orders.RevisionBrief.Compose"/>); its
    /// <see cref="RunEntity.ParentRunId"/> is that shown run, so the report carries over its open points, and the rules of the first
    /// order block as in any change. <see cref="ResumeOptions.UseSeedDraft"/> does not apply to such a run. It is stored under the
    /// same conditions as a change (no other run active, the shown version unchanged, the order not stopped, at most
    /// <see cref="Core.Configuration.OrderOptions.MaxVersionsPerOrder"/> versions), otherwise <see cref="RunNotResumableException"/> with the reason;
    /// the switch <see cref="Core.Configuration.OrderOptions.Revise"/> is not asked, since the change was already accepted.
    /// Such a run is never resumed when it already has its own text (<see cref="Orders.RevisionLines.ResumeChangedText"/>, see
    /// <see cref="ResumeRules.ResumeRefusal"/>: the same change would be made twice) or when a later version of its order has a text
    /// (<see cref="Orders.RevisionLines.ResumeLaterVersion"/>: the older change would be put on the newer text); both throw
    /// <see cref="RunNotResumableException"/> and create no run. The sentences point to "Change something" on the order page.
    /// </summary>
    Task<Guid> ResumeRunAsync(
        ResumeOptions options,
        string? requestingUsername,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes a run and all its associated data (iterations, findings, events, costs, artifacts).
    /// When it was the last run of its order, the order is deleted too.
    /// Returns <c>false</c> if the run does not exist or belongs to a different user (when
    /// <paramref name="requestingUsername"/> is non-null). Pass <c>null</c> to bypass the ownership check.
    /// Throws <see cref="InvalidOperationException"/> if the run is still Pending or Running.
    /// </summary>
    Task<bool> DeleteRunAsync(
        Guid runId,
        string? requestingUsername,
        CancellationToken cancellationToken = default);
}
