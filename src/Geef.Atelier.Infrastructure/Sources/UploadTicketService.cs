using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Persistence.Sources;
using Geef.Atelier.Infrastructure.Sources.Extraction;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Sources;

/// <summary>
/// Upload tickets over <see cref="IUploadTicketRepository"/>: the token is 32 random bytes in base64url, handed out once and
/// stored only as its SHA-256 hex; a claim moves an open ticket to receiving in one statement, so one upload per ticket runs.
/// Files go through <see cref="ISourceIntakeService"/> with the owner of the ticket, which counts refusals itself. Only a file that
/// was checked and refused uses an attempt (<see cref="SourceRefusalCodes.CountsAsAttempt"/>); overload, a broken-off upload, a
/// server error or a cancelled request give the attempt back. A file too long to use completely is refused here, because pages can
/// be chosen only in the web app. A ticket left receiving after a crash is released when it is used again, with the same deadline
/// as the cleanup. The token is never logged.
/// </summary>
internal sealed class UploadTicketService(
    IUploadTicketRepository tickets,
    ISourceIntakeService intake,
    ISourceRepository sources,
    ISourceRefusalLog refusals,
    IOptions<UploadOptions> uploadOptions,
    IOptions<SourceOptions> sourceOptions,
    TimeProvider time,
    ILogger<UploadTicketService> logger) : IUploadTicketService
{
    internal const int TokenBytes = 32;
    internal const int TokenLength = 43;
    internal const int Status201Created = 201;
    internal const int Status400BadRequest = 400;
    internal const int Status404NotFound = 404;
    internal const int Status409Conflict = 409;
    internal const int Status413PayloadTooLarge = 413;
    internal const int Status422UnprocessableEntity = 422;
    internal const int Status429TooManyRequests = 429;
    internal const int Status503ServiceUnavailable = 503;
    private const int MaxFileNameLength = 255;
    private static readonly TimeSpan StaleGrace = TimeSpan.FromMinutes(1);

    public bool Enabled => uploadOptions.Value.Tickets == UploadTicketsMode.On;

    public async Task<PreparedUpload> PrepareAsync(string owner, string fileName, long? sizeBytes, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (!Enabled)
            throw new UploadTicketException(UploadTicketLines.NotAvailable);

        var trimmed = fileName?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > MaxFileNameLength)
            throw new UploadTicketException(UploadTicketLines.FileNameInvalid);

        var maxFileBytes = sourceOptions.Value.MaxFileBytes;
        long? announced = sizeBytes is > 0 ? sizeBytes : null;
        if (announced > maxFileBytes)
        {
            await refusals.RecordAsync(new SourceRefusalRecord(owner, RefusalPath.McpTicket, SourceRefusalCodes.TooLarge, null,
                SourceRefusalCodes.ExtensionOf(trimmed), null, announced), CancellationToken.None);
            throw new UploadTicketException(string.Format(CultureInfo.InvariantCulture, UploadTicketLines.TooLargeFormat, MegabytesOf(maxFileBytes)));
        }

        var opts = uploadOptions.Value;
        var now = time.GetUtcNow();
        var token = NewToken();
        var tokenExpiresAt = now + opts.TokenLifetime;
        var pageExpiresAt = now + (opts.PageLifetime > opts.TokenLifetime ? opts.PageLifetime : opts.TokenLifetime);
        var ticket = new UploadTicketEntity
        {
            Id = Guid.NewGuid(),
            OwnerUsername = owner,
            FileName = SourceFileNames.Sanitize(trimmed),
            DeclaredBytes = announced,
            TokenHash = HashOf(token),
            State = UploadTicketState.Open,
            Attempts = 0,
            CreatedAt = now,
            TokenExpiresAt = tokenExpiresAt,
            ExpiresAt = pageExpiresAt,
        };

        if (!await tickets.AddWithinLimitsAsync(ticket, opts.MaxOpenTicketsPerUser, opts.MaxTicketsPerDay, ct))
            throw new UploadTicketException(UploadTicketLines.TooManyTickets);

        logger.LogInformation("Upload ticket {UploadId} was prepared.", ticket.Id);
        return new PreparedUpload(ticket.Id, token, tokenExpiresAt, pageExpiresAt, announced ?? maxFileBytes);
    }

    public async Task<UploadOutcome> ReceiveByTokenAsync(Guid uploadId, string token, Stream body, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!Enabled || !LooksLikeToken(token))
            return NotValid();

        var hash = HashOf(token);
        var now = time.GetUtcNow();
        await tickets.ResetStaleReceivingAsync(uploadId, StaleBefore(now, sourceOptions.Value), uploadOptions.Value.MaxAttempts, ct);
        var ticket = await tickets.TryClaimByTokenAsync(uploadId, hash, now, uploadOptions.Value.MaxAttempts, ct);
        if (ticket is null)
        {
            return await tickets.ProbeByTokenAsync(uploadId, hash, now, ct) == UploadClaimProbe.Receiving
                ? new UploadOutcome(Status409Conflict, null, null, UploadTicketLines.AlreadyRunning)
                : NotValid();
        }

        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(ticket.TokenHash), Encoding.ASCII.GetBytes(hash)))
        {
            await tickets.ReleaseAsync(ticket.Id, ClaimOf(ticket), uploadOptions.Value.MaxAttempts, null, CancellationToken.None);
            return NotValid();
        }

        return await ReceiveClaimedAsync(ticket, ticket.FileName, body, SourceOrigin.McpAttachment, RefusalPath.McpTicket, ticket.DeclaredBytes, ct);
    }

    public async Task<UploadOutcome> ReceiveFromPageAsync(Guid uploadId, string owner, string fileName, Stream body, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(body);
        if (!Enabled)
            return new UploadOutcome(Status404NotFound, null, null, null);

        var now = time.GetUtcNow();
        await tickets.ResetStaleReceivingAsync(uploadId, StaleBefore(now, sourceOptions.Value), uploadOptions.Value.MaxAttempts, ct);
        var ticket = await tickets.TryClaimByOwnerAsync(uploadId, owner, now, uploadOptions.Value.MaxAttempts, ct);
        if (ticket is null)
        {
            return await ProbeAsync(uploadId, owner, ct) == UploadClaimProbe.Receiving
                ? new UploadOutcome(Status409Conflict, null, null, UploadTicketLines.AlreadyRunning)
                : new UploadOutcome(Status404NotFound, null, null, null);
        }

        var name = string.IsNullOrWhiteSpace(fileName) ? ticket.FileName : fileName;
        return await ReceiveClaimedAsync(ticket, name, body, SourceOrigin.WebUpload, RefusalPath.UploadPage, announcedBytes: null, ct);
    }

    public async Task<UploadClaimProbe> ProbeAsync(Guid uploadId, string owner, CancellationToken ct = default)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(owner))
            return UploadClaimProbe.NotFound;
        var now = time.GetUtcNow();
        var probe = await tickets.ProbeAsync(uploadId, owner, now, ct);
        if (probe != UploadClaimProbe.Receiving
            || !await tickets.ResetStaleReceivingAsync(uploadId, StaleBefore(now, sourceOptions.Value), uploadOptions.Value.MaxAttempts, ct))
            return probe;
        return await tickets.ProbeAsync(uploadId, owner, now, ct);
    }

    public async Task<IReadOnlyList<SourceReceipt>> ResolveAsync(IReadOnlyList<Guid> uploadIds, string owner, Func<Guid, string> pageLink, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(uploadIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(pageLink);
        if (!Enabled)
            throw new UploadTicketException(UploadTicketLines.NotAvailable);

        var ids = uploadIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        var found = (await tickets.FindForOwnerAsync(ids, owner, ct)).ToDictionary(t => t.Id);
        var now = time.GetUtcNow();
        var receipts = new List<SourceReceipt>(ids.Count);
        foreach (var id in ids)
        {
            if (!found.TryGetValue(id, out var ticket))
                throw Refusal(UploadTicketLines.NotFoundFormat, id);

            if (ticket.State != UploadTicketState.Used)
                throw NotArrived(ticket, now, pageLink);

            var receipt = ticket.SourceId is { } sourceId ? await intake.GetReceiptAsync(sourceId, owner, ct) : null;
            receipts.Add(receipt ?? throw Refusal(UploadTicketLines.NoLongerStoredFormat, id));
        }

        return receipts;
    }

    private async Task<UploadOutcome> ReceiveClaimedAsync(UploadTicketEntity ticket, string fileName, Stream body, SourceOrigin origin, RefusalPath path,
        long? announcedBytes, CancellationToken ct)
    {
        var maxAttempts = uploadOptions.Value.MaxAttempts;
        var claim = ClaimOf(ticket);
        var settled = false;
        try
        {
            var receipt = await intake.IntakeAsync(new SourceUpload(fileName, null, 0, body, announcedBytes), ticket.OwnerUsername, origin, ct, path);
            if (!receipt.Usable || receipt.SourceId is not { } sourceId)
            {
                var code = receipt.RefusalCode ?? SourceRefusalCodes.Other;
                await tickets.ReleaseAsync(ticket.Id, claim, maxAttempts, SourceRefusalCodes.CountsAsAttempt(code) ? code : null, CancellationToken.None);
                settled = true;
                return new UploadOutcome(StatusOf(receipt), receipt, null, receipt.Reason);
            }

            if (receipt.BudgetClass == SourceBudgetClass.TooLarge)
            {
                var tooLong = await RefuseTooLongAsync(receipt, ticket.OwnerUsername, fileName, path);
                await tickets.ReleaseAsync(ticket.Id, claim, maxAttempts, SourceRefusalCodes.TooLongForTicket, CancellationToken.None);
                settled = true;
                return new UploadOutcome(Status422UnprocessableEntity, tooLong, null, tooLong.Reason);
            }

            var stored = await sources.GetAsync(sourceId, includeText: false, ct);
            if (!await tickets.CompleteAsync(ticket.Id, claim, sourceId, time.GetUtcNow(), CancellationToken.None))
            {
                settled = true;
                logger.LogWarning("Upload ticket {UploadId} stopped receiving before its file was stored.", ticket.Id);
                return NotValid();
            }

            settled = true;
            logger.LogInformation("Upload ticket {UploadId} received its file.", ticket.Id);
            return new UploadOutcome(Status201Created, receipt, stored?.Sha256, null);
        }
        finally
        {
            if (!settled)
                await tickets.ReleaseAsync(ticket.Id, claim, maxAttempts, null, CancellationToken.None);
        }
    }

    private async Task<SourceReceipt> RefuseTooLongAsync(SourceReceipt receipt, string owner, string fileName, RefusalPath path)
    {
        var limit = sourceOptions.Value.MapMaxChars;
        var removed = true;
        if (!receipt.Duplicate && receipt.SourceId is { } sourceId)
        {
            try
            {
                var deleted = await intake.DeleteAsync(sourceId, owner, CancellationToken.None);
                removed = deleted is not SourceDeleteResult.InUse;
                if (!removed)
                    logger.LogWarning("A file too long for an upload ticket could not be removed again: {Error}", deleted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                removed = false;
                logger.LogWarning("A file too long for an upload ticket could not be removed again: {Error}", ex.GetType().Name);
            }
        }

        var reason = string.Format(CultureInfo.InvariantCulture,
            receipt.PageCount is not null ? UploadTicketLines.TooLongWithPagesFormat : UploadTicketLines.TooLongFormat,
            receipt.CharCount.ToString("N0", CultureInfo.InvariantCulture), limit.ToString("N0", CultureInfo.InvariantCulture),
            removed ? UploadTicketLines.NothingStored : UploadTicketLines.NotRemovedYet);

        await refusals.RecordAsync(new SourceRefusalRecord(owner, path, SourceRefusalCodes.TooLongForTicket, receipt.Kind,
            SourceRefusalCodes.ExtensionOf(fileName), null, receipt.SizeBytes), CancellationToken.None);
        return receipt with
        {
            SourceId = null,
            CharCount = 0,
            WordCount = 0,
            Quality = SourceQuality.Unreadable,
            Usable = false,
            Duplicate = false,
            BudgetClass = SourceBudgetClass.FullText,
            Preview = string.Empty,
            Warnings = [],
            Reason = reason,
            EstimatedTokens = 0,
            RefusalCode = SourceRefusalCodes.TooLongForTicket,
        };
    }

    private UploadTicketException NotArrived(UploadTicketEntity ticket, DateTimeOffset now, Func<Guid, string> pageLink)
    {
        var ended = ticket.State == UploadTicketState.Failed || ticket.ExpiresAt <= now;
        var reason = ticket.LastRefusalCode is { } code ? ReasonOf(code) : null;
        var text = (ended, reason) switch
        {
            (true, null) => string.Format(CultureInfo.InvariantCulture, UploadTicketLines.NoUsableFileFormat, ticket.Id),
            (true, _) => string.Format(CultureInfo.InvariantCulture, UploadTicketLines.NoUsableFileRefusedFormat, ticket.Id, reason),
            (false, null) => string.Format(CultureInfo.InvariantCulture, UploadTicketLines.NotArrivedFormat, ticket.Id, pageLink(ticket.Id)),
            (false, _) => string.Format(CultureInfo.InvariantCulture, UploadTicketLines.RefusedSoFarFormat, ticket.Id, reason, pageLink(ticket.Id)),
        };
        return new UploadTicketException(text);
    }

    /// <summary>
    /// A fixed sentence for a stored refusal code, from the constants of detection and extraction; never text taken from the file or
    /// from a process that read it.
    /// </summary>
    internal string ReasonOf(string code) => code switch
    {
        SourceRefusalCodes.PdfNoText => PdfSourceExtractor.NoTextReason,
        SourceRefusalCodes.PdfEncrypted => PdfSourceExtractor.EncryptedReason,
        SourceRefusalCodes.UnsupportedType => SourceKindDetector.UnsupportedReason,
        SourceRefusalCodes.OldWord => SourceKindDetector.OldWordReason,
        SourceRefusalCodes.ArchiveRefused => DocxSourceExtractor.ArchiveReason,
        SourceRefusalCodes.TooComplex => IsolatedExtractionProcess.TooComplexReason,
        SourceRefusalCodes.Unreadable => IsolatedExtractionProcess.FailedReason,
        SourceRefusalCodes.Empty => SourceIntakeService.NoReadableTextReason,
        SourceRefusalCodes.Timeout => IsolatedExtractionProcess.TooSlowReason,
        SourceRefusalCodes.TooMuchText => SourceExtraction.TooMuchTextReason,
        SourceRefusalCodes.TooManyPages => "This file has too many pages.",
        SourceRefusalCodes.TooLarge => string.Format(CultureInfo.InvariantCulture, "This file is larger than {0} MB.", MegabytesOf(sourceOptions.Value.MaxFileBytes)),
        SourceRefusalCodes.SizeMismatch => "The file did not have the size that was announced.",
        SourceRefusalCodes.TooLongForTicket => "This file is too long to use completely when it is added this way. Use a shorter file, or start the order in the Atelier web app and choose the pages there.",
        _ => "This file could not be used.",
    };

    private static DateTimeOffset StaleBefore(DateTimeOffset now, SourceOptions options) => now - StaleAfter(options);

    /// <summary>How long a ticket may stay receiving before it counts as left behind by a crash: the upload timeout plus a minute.</summary>
    internal static TimeSpan StaleAfter(SourceOptions options) => options.UploadTimeout + StaleGrace;

    private static int StatusOf(SourceReceipt receipt) => receipt.Refusal switch
    {
        SourceRefusal.TooLarge => Status413PayloadTooLarge,
        SourceRefusal.Interrupted => Status400BadRequest,
        SourceRefusal.TooManyUploads => Status429TooManyRequests,
        SourceRefusal.Busy => Status503ServiceUnavailable,
        _ => Status422UnprocessableEntity,
    };

    private static DateTimeOffset ClaimOf(UploadTicketEntity ticket) =>
        ticket.ReceivingSince ?? throw new InvalidOperationException($"Upload ticket {ticket.Id} was claimed without a start time.");

    private static UploadOutcome NotValid() => new(Status404NotFound, null, null, UploadTicketLines.LinkNotValid);

    private static UploadTicketException Refusal(string format, Guid id) =>
        new(string.Format(CultureInfo.InvariantCulture, format, id));

    private static string MegabytesOf(long bytes) => (bytes / 1_048_576.0).ToString("0.#", CultureInfo.InvariantCulture);

    internal static string NewToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));

    internal static string HashOf(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(token)));

    internal static bool LooksLikeToken(string? token) =>
        token is { Length: TokenLength } && token.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_');
}
