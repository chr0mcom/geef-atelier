using Geef.Atelier.Core.Domain.Sources;

namespace Geef.Atelier.Core.Persistence.Sources;

/// <summary>
/// Persistence of upload tickets. Every claim is one atomic statement, so that at most one upload of a ticket runs and a
/// ticket is used at most once. Lookups by token compare the stored SHA-256 hex, never a token.
/// </summary>
public interface IUploadTicketRepository
{
    /// <summary>Inserts a ticket.</summary>
    Task AddAsync(UploadTicketEntity ticket, CancellationToken ct = default);

    /// <summary>
    /// Inserts <paramref name="ticket"/> unless its owner already has <paramref name="maxOpen"/> open or receiving tickets whose
    /// page has not expired, or prepared <paramref name="maxPerDay"/> tickets in the 24 hours before its creation. Counting and
    /// inserting hold a lock per owner, so parallel calls never pass the limits together. Returns whether it was inserted.
    /// </summary>
    Task<bool> AddWithinLimitsAsync(UploadTicketEntity ticket, int maxOpen, int maxPerDay, CancellationToken ct = default);

    /// <summary>Open or receiving tickets of <paramref name="owner"/> whose page has not expired, and all tickets prepared in the 24 hours before <paramref name="now"/>.</summary>
    Task<(int Open, int Last24h)> CountForOwnerAsync(string owner, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    /// Sets an open ticket with this id and token hash, whose token has not expired and that has attempts left, to
    /// <see cref="UploadTicketState.Receiving"/> and counts the attempt; returns the claimed ticket or <c>null</c>.
    /// </summary>
    Task<UploadTicketEntity?> TryClaimByTokenAsync(Guid id, string tokenHash, DateTimeOffset now, int maxAttempts, CancellationToken ct = default);

    /// <summary>
    /// Sets an open ticket of <paramref name="owner"/> with this id, whose page has not expired and that has attempts left,
    /// to <see cref="UploadTicketState.Receiving"/> and counts the attempt; returns the claimed ticket or <c>null</c>.
    /// </summary>
    Task<UploadTicketEntity?> TryClaimByOwnerAsync(Guid id, string owner, DateTimeOffset now, int maxAttempts, CancellationToken ct = default);

    /// <summary>How the ticket looks to <paramref name="owner"/>; <see cref="UploadClaimProbe.NotFound"/> for unknown and foreign tickets alike.</summary>
    Task<UploadClaimProbe> ProbeAsync(Guid id, string owner, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>How the ticket looks to the holder of the token with <paramref name="tokenHash"/>; <see cref="UploadClaimProbe.NotFound"/> when id and hash do not match.</summary>
    Task<UploadClaimProbe> ProbeByTokenAsync(Guid id, string tokenHash, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    /// Sets the ticket to <see cref="UploadTicketState.Used"/> with its source, if it is still receiving under the claim that started at
    /// <paramref name="receivingSince"/>; false when the ticket was released or claimed again in the meantime.
    /// </summary>
    Task<bool> CompleteAsync(Guid id, DateTimeOffset receivingSince, Guid sourceId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    /// Ends the claim that started at <paramref name="receivingSince"/>, if the ticket is still receiving under it; a later claim is
    /// never released. With <paramref name="checkedRefusalCode"/> the file was checked and refused: the attempt stays counted, the code
    /// is kept in <see cref="UploadTicketEntity.LastRefusalCode"/> and the ticket goes back to <see cref="UploadTicketState.Open"/>, or
    /// to <see cref="UploadTicketState.Failed"/> when it has no attempts left. Without it the file was not checked (overload, server
    /// error, broken-off upload): the attempt is given back and the ticket is open again.
    /// </summary>
    Task ReleaseAsync(Guid id, DateTimeOffset receivingSince, int maxAttempts, string? checkedRefusalCode, CancellationToken ct = default);

    /// <summary>The tickets of <paramref name="owner"/> among <paramref name="ids"/>; foreign and unknown ids are left out.</summary>
    Task<IReadOnlyList<UploadTicketEntity>> FindForOwnerAsync(IReadOnlyCollection<Guid> ids, string owner, CancellationToken ct = default);

    /// <summary>
    /// Releases tickets that are receiving since before <paramref name="olderThan"/> (after a crash or restart); returns their number.
    /// The attempt stays counted, so a file that brings the process down cannot be sent again and again; the ticket goes back to
    /// <see cref="UploadTicketState.Open"/>, or to <see cref="UploadTicketState.Failed"/> when it has no attempts left.
    /// </summary>
    Task<int> ResetStaleReceivingAsync(DateTimeOffset olderThan, int maxAttempts, CancellationToken ct = default);

    /// <summary>
    /// Releases the ticket <paramref name="id"/> if it is receiving since before <paramref name="olderThan"/>, as
    /// <see cref="ResetStaleReceivingAsync(DateTimeOffset, int, CancellationToken)"/> does for all; returns whether it was released.
    /// </summary>
    Task<bool> ResetStaleReceivingAsync(Guid id, DateTimeOffset olderThan, int maxAttempts, CancellationToken ct = default);

    /// <summary>Deletes tickets whose page expired before <paramref name="before"/>; returns their number.</summary>
    Task<int> DeleteExpiredAsync(DateTimeOffset before, CancellationToken ct = default);
}
