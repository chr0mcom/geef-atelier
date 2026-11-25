using Geef.Atelier.Core.Domain.Orders;

namespace Geef.Atelier.Application.Orders;

/// <summary>Application service for orders, the unit the person sees. Runs stay the log underneath. <c>requestingUsername = null</c> bypasses the ownership check (admin), as in <see cref="Runs.IRunService"/>.</summary>
public interface IOrderService
{
    /// <summary>
    /// Validates the request and the sources (owner, count, page selection, budget), creates the order and, because there is no intake step
    /// in this stage, moves it straight to <see cref="OrderState.Ready"/> and creates version 1 of its run in the same transaction.
    /// Throws <see cref="OrderSubmissionException"/> for problems the person can fix.
    /// </summary>
    Task<OrderSubmission> SubmitAsync(SubmitOrderRequest request, CancellationToken ct = default);

    /// <summary>
    /// Measures <paramref name="sources"/> with their page selections the same way as <see cref="SubmitAsync"/> does, without
    /// creating anything. A problem the person can fix (a file that is not found, pages that do not exist, pages chosen for a
    /// file that is not a PDF) comes back as <see cref="SourceSelectionCheck.Problem"/>.
    /// </summary>
    Task<SourceSelectionCheck> CheckSourcesAsync(IReadOnlyList<OrderSourceSelection> sources, string ownerUsername, CancellationToken ct = default);

    /// <summary>Returns the order page data, or <c>null</c> when the order is unknown or foreign.</summary>
    Task<OrderView?> GetAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default);

    /// <summary>Returns the delivered text with placeholders, open checks, used sources and warnings, or <c>null</c> when the order is unknown or foreign.</summary>
    Task<OrderResult?> GetResultAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default);

    /// <summary>
    /// Like <see cref="GetResultAsync(Guid, string?, CancellationToken)"/>; <paramref name="requestingUsername"/> is the owner filter
    /// (<c>null</c> for administrators), <paramref name="viewerUsername"/> the user name of whoever looks at the result, also for an
    /// administrator. Passages from the About me details are shown only when the viewer ordered the text.
    /// </summary>
    Task<OrderResult?> GetResultAsync(Guid orderId, string? requestingUsername, string? viewerUsername, CancellationToken ct = default) =>
        GetResultAsync(orderId, requestingUsername, ct);

    /// <summary>Lists orders newest activity first. <paramref name="ownerUsername"/> = null lists all users (admin view).</summary>
    Task<IReadOnlyList<OrderListItem>> ListAsync(string? ownerUsername, int limit = 50, CancellationToken ct = default);

    /// <summary>Returns as soon as the display state differs from <paramref name="knownState"/> or is finished, at the latest after <paramref name="maxWait"/>. Polls the database; never longer than <c>Orders:MaxWaitSeconds</c>.</summary>
    Task<OrderView?> WaitForChangeAsync(Guid orderId, string? requestingUsername, OrderDisplayState? knownState, TimeSpan maxWait, CancellationToken ct = default);

    /// <summary>
    /// Stops the order: before a run exists the state becomes <see cref="OrderState.Cancelled"/>; with an active run the run is
    /// cancelled. Asking again while the run is still stopping returns true as well.
    /// </summary>
    Task<bool> CancelAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default);

    /// <summary>
    /// Deletes a finished or stopped order with its runs; sources that no other order uses are deleted with their files.
    /// Throws <see cref="OrderStillWorkingException"/> while a run of the order is still working.
    /// </summary>
    Task<bool> DeleteAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default);

    /// <summary>
    /// Stores the answers of an order that waits for them and starts its first run in the same transaction. Questions without an
    /// answer count as "decide for me". Throws <see cref="OrderAnswerException"/> for invalid answers. When the order no longer waits
    /// (already started, stopped), nothing changes and the current state is returned with a notice, so that a repeated call is harmless.
    /// </summary>
    Task<OrderSubmission> AnswerAsync(AnswerOrderRequest request, CancellationToken ct = default);

    /// <summary>
    /// Starts the order without (further) questions: while it is being analysed with the request as written, while it waits for answers
    /// with the assumptions. Idempotent like <see cref="AnswerAsync"/>.
    /// </summary>
    Task<OrderSubmission> SkipClarificationAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default);

    /// <summary>
    /// Starts the next version of the order, seeded with the text of the shown version and the change of the person, without questions
    /// and always on the direct path. Throws <see cref="OrderRevisionException"/> for a change that cannot be made. While a version is
    /// still being written nothing changes and the answer carries <see cref="RevisionLines.Busy"/>, so that a repeated call is harmless.
    /// </summary>
    Task<OrderRevision> ReviseAsync(ReviseOrderRequest request, CancellationToken ct = default);
}
