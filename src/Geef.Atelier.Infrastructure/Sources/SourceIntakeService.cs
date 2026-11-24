using System.Globalization;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Dashboard;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Persistence.Sources;
using Geef.Atelier.Infrastructure.Sources.Extraction;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Sources;

/// <summary>
/// Receives one file at a time: takes one of the owner's upload places and an upload slot before reading anything, streams
/// the file into a private temporary file (hashing on the way), reuses an identical file of the same owner, reads the text
/// through <see cref="ISourceExtractionRunner"/>, refuses text beyond <see cref="SourceOptions.MaxExtractedChars"/> before
/// copying it, prepares it in one pass (line ends, page-marker look-alikes, long lines, counts) and stores it with the
/// original under the per-user quota of unattached sources (files, bytes and characters, checked again under a lock).
/// Pasted text needs no upload place or slot.
/// </summary>
internal sealed class SourceIntakeService(
    ISourceRepository repository,
    ISourceRetentionStore retention,
    ISourceFileStore files,
    ISourceExtractionRunner extraction,
    SourceIntakeLimiter limiter,
    IOptions<SourceOptions> options,
    ILogger<SourceIntakeService> logger,
    TimeProvider? timeProvider = null) : ISourceIntakeService
{
    internal const string DuplicateMessage = "You added this file before; the earlier copy is used.";
    internal const string MapMessage = "Large file: parts will be searched, not read in full.";
    internal const string SaveFailedReason = "The file could not be saved. Please try again.";
    internal const string InterruptedReason = "The upload was interrupted. Please try again.";

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public Task<SourceReceipt> IntakeAsync(SourceUpload upload, string ownerUsername, SourceOrigin origin, CancellationToken ct = default) =>
        IntakeCoreAsync(upload, ownerUsername, origin, limited: true, ct);

    private async Task<SourceReceipt> IntakeCoreAsync(SourceUpload upload, string ownerUsername, SourceOrigin origin, bool limited, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUsername);

        var opts = options.Value;
        var fileName = SourceFileNames.Sanitize(upload.FileName);
        var declared = Math.Max(0, upload.Length);
        if (declared > opts.MaxFileBytes)
            return Refused(fileName, declared, TooLargeReason(opts), refusal: SourceRefusal.TooLarge);

        using var place = limited ? limiter.TryEnterUser(ownerUsername) : null;
        if (limited && place is null)
            return Refused(fileName, declared, limiter.UserBusyReason, refusal: SourceRefusal.TooManyUploads);

        using var slot = limited ? await limiter.EnterIntakeAsync(ct) : null;
        if (limited && slot is null)
        {
            logger.LogWarning("An upload was refused because all upload slots stayed busy.");
            return Refused(fileName, declared, SourceIntakeLimiter.BusyReason, refusal: SourceRefusal.Busy);
        }

        IncomingUpload? received;
        try
        {
            received = await IncomingUpload.ReceiveAsync(upload.Content, opts.StorageRoot, opts.MaxFileBytes, ct);
        }
        catch (UploadInterruptedException ex)
        {
            logger.LogInformation("An upload ended before its last byte: {Error}", ex.InnerException?.GetType().Name);
            return Refused(fileName, declared, InterruptedReason, refusal: SourceRefusal.Interrupted);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError("An upload could not be written to the sources volume: {Error}", ex.GetType().Name);
            return Refused(fileName, declared, SaveFailedReason);
        }

        if (received is null)
            return Refused(fileName, declared, TooLargeReason(opts), refusal: SourceRefusal.TooLarge);

        await using (received)
            return await IntakeReceivedAsync(received, fileName, ownerUsername, origin, opts, ct);
    }

    private async Task<SourceReceipt> IntakeReceivedAsync(IncomingUpload received, string fileName, string ownerUsername, SourceOrigin origin, SourceOptions opts, CancellationToken ct)
    {
        if (received.Length == 0)
            return Refused(fileName, 0, "This file is empty.");

        if (await ReuseAsync(ownerUsername, received.Sha256, opts, ct) is { } reused)
            return reused;

        var quota = Quota(opts);
        var usage = await retention.GetUnattachedUsageAsync(ownerUsername, ct);
        if (!quota.Allows(usage, received.Length, 0))
            return Refused(fileName, received.Length, QuotaReason(usage, received.Length, 0, opts));

        var detected = SourceKindDetector.DetectFile(received.FilePath, received.Head, fileName, opts.MaxArchiveEntries);
        if (detected.Kind is not { } kind)
            return Refused(fileName, received.Length, detected.Reason ?? SourceKindDetector.UnsupportedReason);

        SourceExtraction? result = await extraction.ExtractAsync(kind, received.FilePath, received.Directory, ct);
        if (result.Quality == SourceQuality.Unreadable || string.IsNullOrWhiteSpace(result.Text))
            return Refused(fileName, received.Length, result.FailureReason ?? "This file has no readable text.", kind, result.PageCount, result.Warnings,
                result.FailureReason == SourceIntakeLimiter.BusyReason ? SourceRefusal.Busy : SourceRefusal.None);
        if (result.Text.Length > opts.MaxExtractedChars)
        {
            logger.LogInformation("A {Kind} file was refused because its text is longer than {Limit} characters.", kind, opts.MaxExtractedChars);
            return Refused(fileName, received.Length, SourceExtraction.TooMuchTextReason, kind, result.PageCount);
        }

        var warnings = result.Warnings.ToList();
        var pageCount = result.PageCount;
        var quality = result.Quality;
        var normalized = result.Text.NormalizeForStorage(defuseMarkerLines: kind is not SourceKind.Pdf, opts.MaxLineChars);
        result = null;
        if (normalized.Wrapped && kind is not SourceKind.Pdf)
            warnings.Add(new SourceWarning(SourceWarningCodes.LongLinesWrapped, "Very long lines were split so they can be read."));

        var charCount = normalized.CharCount;
        var budget = SourceBudgetPlanner.Classify(charCount, opts);
        AddBudgetWarning(warnings, budget, charCount, kind, opts);

        var sourceId = Guid.NewGuid();
        string storagePath;
        try
        {
            storagePath = await files.SaveFromFileAsync(ownerUsername, sourceId, kind.StorageExtension(), received.FilePath, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogError("Source file could not be stored: {Error}", ex.GetType().Name);
            return Refused(fileName, received.Length, SaveFailedReason, kind, pageCount, warnings);
        }

        var now = _time.GetUtcNow();
        var source = new SourceEntity
        {
            Id = sourceId,
            OwnerUsername = ownerUsername,
            FileName = fileName,
            ContentType = kind.ContentType(),
            Kind = kind,
            SizeBytes = received.Length,
            Sha256 = received.Sha256,
            StoragePath = storagePath,
            ExtractedText = normalized.Text,
            PageCount = pageCount,
            CharCount = charCount,
            WordCount = normalized.WordCount,
            Quality = quality,
            Warnings = warnings,
            Origin = origin,
            ExpiresAt = now + opts.UnattachedRetention,
            CreatedAt = now,
        };

        SourceUsage? refusedBy;
        try
        {
            refusedBy = await retention.AddWithinQuotaAsync(source, quota, ct);
        }
        catch (SourceAlreadyStoredException)
        {
            files.TryDelete(storagePath);
            return await ReuseAsync(ownerUsername, received.Sha256, opts, ct)
                   ?? Refused(fileName, received.Length, SaveFailedReason, kind, pageCount, warnings);
        }
        catch
        {
            files.TryDelete(storagePath);
            throw;
        }

        if (refusedBy is { } over)
        {
            files.TryDelete(storagePath);
            return Refused(fileName, received.Length, QuotaReason(over, received.Length, charCount, opts), kind, pageCount);
        }

        return ToReceipt(source, duplicate: false);
    }

    private async Task<SourceReceipt?> ReuseAsync(string ownerUsername, string sha, SourceOptions opts, CancellationToken ct)
    {
        var existing = await repository.FindByHashAsync(ownerUsername, sha, ct);
        if (existing is null)
            return null;

        await retention.ExtendExpiryAsync(existing.Id, _time.GetUtcNow() + opts.UnattachedRetention, ct);
        var stored = await repository.GetAsync(existing.Id, includeText: true, ct);
        return stored is null ? null : ToReceipt(stored, duplicate: true);
    }

    private static SourceQuota Quota(SourceOptions opts) =>
        new(opts.MaxUnattachedSourcesPerUser, opts.MaxUnattachedBytesPerUser, opts.MaxUnattachedCharsPerUser);

    private static string QuotaReason(SourceUsage usage, long incomingBytes, long incomingChars, SourceOptions opts)
    {
        if (usage.Count + 1 > opts.MaxUnattachedSourcesPerUser)
            return string.Create(CultureInfo.InvariantCulture,
                $"You have {usage.Count:N0} files that are not used in an order yet. Please try again later or contact an administrator.");
        if (usage.Bytes + incomingBytes > opts.MaxUnattachedBytesPerUser)
            return string.Create(CultureInfo.InvariantCulture,
                $"Your files that are not used in an order yet would take more than {opts.MaxUnattachedBytesPerUser / 1_048_576.0:0.#} MB. Please try again later or contact an administrator.");
        return string.Create(CultureInfo.InvariantCulture,
            $"Your files that are not used in an order yet would hold more than {opts.MaxUnattachedCharsPerUser:N0} characters of text. Please try again later or contact an administrator.");
    }

    public Task<SourceReceipt> IntakeTextAsync(string title, string text, string ownerUsername, SourceOrigin origin, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        var name = SourceFileNames.Sanitize(string.IsNullOrWhiteSpace(title) ? "Pasted text" : title);
        if (Path.GetExtension(name).Length == 0)
            name += ".txt";
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        return IntakeCoreAsync(new SourceUpload(name, "text/plain", bytes.Length, new MemoryStream(bytes, writable: false)), ownerUsername, origin, limited: false, ct);
    }

    public async Task<SourceReceipt?> GetReceiptAsync(Guid sourceId, string? requestingUsername, CancellationToken ct = default)
    {
        var source = await repository.GetAsync(sourceId, includeText: true, ct);
        if (source is null || (requestingUsername is not null && source.OwnerUsername != requestingUsername))
            return null;
        return ToReceipt(source, duplicate: false);
    }

    public async Task<SourceDeleteResult> DeleteAsync(Guid sourceId, string? requestingUsername, CancellationToken ct = default)
    {
        var source = await repository.GetAsync(sourceId, includeText: false, ct);
        if (source is null || (requestingUsername is not null && source.OwnerUsername != requestingUsername))
            return SourceDeleteResult.NotFound;
        if (await repository.IsAttachedAsync(sourceId, ct) || !await repository.DeleteIfUnattachedAsync(sourceId, ct))
            return await repository.GetAsync(sourceId, includeText: false, ct) is null ? SourceDeleteResult.NotFound : SourceDeleteResult.InUse;

        if (source.StoragePath is { } path)
            files.TryDelete(path);
        return SourceDeleteResult.Deleted;
    }

    internal SourceReceipt ToReceipt(SourceEntity source, bool duplicate)
    {
        var opts = options.Value;
        var warnings = source.Warnings.ToList();
        if (duplicate)
            warnings.Add(new SourceWarning(SourceWarningCodes.Duplicate, DuplicateMessage));
        var budget = SourceBudgetPlanner.Classify(source.CharCount, opts);
        if (warnings.All(w => w.Code is not (SourceWarningCodes.UsesDocumentMap or SourceWarningCodes.OverBudget)))
            AddBudgetWarning(warnings, budget, source.CharCount, source.Kind, opts);

        return new SourceReceipt(
            source.Id,
            source.FileName,
            source.Kind,
            source.SizeBytes,
            source.PageCount,
            source.CharCount,
            source.WordCount,
            source.Quality,
            Usable: true,
            Duplicate: duplicate,
            budget,
            source.ExtractedText.PreviewWithoutPageMarkers(opts.PreviewChars),
            warnings,
            Reason: null,
            EstimatedTokens: SourceBudgetPlanner.EstimateTokens(source.CharCount, opts));
    }

    private static void AddBudgetWarning(List<SourceWarning> warnings, SourceBudgetClass budget, int chars, SourceKind kind, SourceOptions opts)
    {
        if (budget == SourceBudgetClass.WithMap)
            warnings.Add(new SourceWarning(SourceWarningCodes.UsesDocumentMap, MapMessage));
        else if (budget == SourceBudgetClass.TooLarge)
            warnings.Add(new SourceWarning(SourceWarningCodes.OverBudget, kind == SourceKind.Pdf
                ? string.Create(CultureInfo.InvariantCulture,
                    $"This file is too long to use completely ({chars:N0} characters, the limit is {opts.MapMaxChars:N0}). Choose the pages to use.")
                : string.Create(CultureInfo.InvariantCulture,
                    $"This file is too long ({chars:N0} characters, the limit is {opts.MapMaxChars:N0}). Please use a shorter version.")));
    }

    private static SourceReceipt Refused(string fileName, long size, string reason, SourceKind? kind = null, int? pageCount = null,
        IReadOnlyList<SourceWarning>? warnings = null, SourceRefusal refusal = SourceRefusal.None) =>
        new(null, fileName, kind, size, pageCount, 0, 0, SourceQuality.Unreadable, Usable: false, Duplicate: false,
            SourceBudgetClass.FullText, string.Empty, warnings ?? [], reason, Refusal: refusal);

    private static string TooLargeReason(SourceOptions opts) =>
        opts.MaxFileBytes >= 1_048_576
            ? string.Create(CultureInfo.InvariantCulture, $"This file is larger than {opts.MaxFileBytes / 1_048_576.0:0.#} MB.")
            : string.Create(CultureInfo.InvariantCulture, $"This file is larger than {Math.Max(1, opts.MaxFileBytes / 1_024)} KB.");
}
