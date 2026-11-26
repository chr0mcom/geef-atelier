using Geef.Atelier.Core.Domain.Sources;

namespace Geef.Atelier.Application.Sources;

/// <summary>
/// A prepared upload: the ticket id (not secret), the one-time token in plain text (only in this answer, never stored), when
/// the token and the upload page expire, and the largest accepted file.
/// </summary>
public sealed record PreparedUpload(Guid UploadId, string Token, DateTimeOffset TokenExpiresAt, DateTimeOffset PageExpiresAt, long MaxBytes);

/// <summary>
/// Answer to an upload: the HTTP status for the endpoint (201, 404, 409, 413, 422, …), the receipt when the file was read,
/// the SHA-256 of the received bytes when they were received completely, and the sentence for the caller when it was refused.
/// </summary>
public sealed record UploadOutcome(int StatusCode, SourceReceipt? Receipt, string? Sha256, string? Reason);

/// <summary>A refusal of <see cref="IUploadTicketService"/> whose message is a sentence from <see cref="UploadTicketLines"/>.</summary>
public sealed class UploadTicketException(string message) : InvalidOperationException(message);

/// <summary>
/// Upload tickets: a person's assistant prepares a ticket, the file arrives with the token (<c>curl</c>) or on the upload page
/// of the signed-in owner, and the order names the ticket. Every file goes through <see cref="ISourceIntakeService"/>; the
/// owner always comes from the ticket or the sign-in, never from the caller.
/// </summary>
public interface IUploadTicketService
{
    /// <summary>True while <c>Uploads:Tickets</c> is <c>On</c>.</summary>
    bool Enabled { get; }

    /// <summary>
    /// Prepares a ticket for one file of <paramref name="owner"/>. Throws <see cref="UploadTicketException"/> when the name is
    /// unusable, <paramref name="sizeBytes"/> is above the file limit or the person has too many tickets.
    /// </summary>
    Task<PreparedUpload> PrepareAsync(string owner, string fileName, long? sizeBytes, CancellationToken ct = default);

    /// <summary>Receives the file of the ticket <paramref name="uploadId"/> with its token; unknown, foreign, expired and used tickets look the same (404).</summary>
    Task<UploadOutcome> ReceiveByTokenAsync(Guid uploadId, string token, Stream body, CancellationToken ct = default);

    /// <summary>Receives the file of the ticket <paramref name="uploadId"/> from the upload page of its signed-in <paramref name="owner"/>.</summary>
    Task<UploadOutcome> ReceiveFromPageAsync(Guid uploadId, string owner, string fileName, Stream body, CancellationToken ct = default);

    /// <summary>How the ticket looks to <paramref name="owner"/> (upload page).</summary>
    Task<UploadClaimProbe> ProbeAsync(Guid uploadId, string owner, CancellationToken ct = default);

    /// <summary>
    /// The receipts of the arrived files of <paramref name="uploadIds"/>, all of <paramref name="owner"/>, in the given order.
    /// Throws <see cref="UploadTicketException"/> with a sentence of <see cref="UploadTicketLines"/> when any id is unknown or
    /// foreign, has not arrived or is no longer stored; <paramref name="pageLink"/> gives the upload page named in that sentence.
    /// </summary>
    Task<IReadOnlyList<SourceReceipt>> ResolveAsync(IReadOnlyList<Guid> uploadIds, string owner, Func<Guid, string> pageLink, CancellationToken ct = default);
}
