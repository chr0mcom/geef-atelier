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
/// Receives one file at a time: streams it into a private temporary file (hashing on the way), reuses an identical file of
/// the same owner, enforces the per-user quota of unattached sources, reads the text through
/// <see cref="ISourceExtractionRunner"/>, neutralises page-marker look-alikes and moves the original into the store.
/// At most <see cref="SourceOptions.MaxConcurrentIntakes"/> uploads run at the same time.
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

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<SourceReceipt> IntakeAsync(SourceUpload upload, string ownerUsername, SourceOrigin origin, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUsername);

        var opts = options.Value;
        var fileName = SourceFileNames.Sanitize(upload.FileName);
        if (upload.Length > opts.MaxFileBytes)
            return Refused(fileName, upload.Length, TooLargeReason(opts));

        using var slot = await limiter.EnterIntakeAsync(ct);
        if (slot is null)
        {
            logger.LogWarning("An upload was refused because all upload slots stayed busy.");
            return Refused(fileName, upload.Length, SourceIntakeLimiter.BusyReason);
        }

        IncomingUpload? received;
        try
        {
            received = await IncomingUpload.ReceiveAsync(upload.Content, opts.StorageRoot, opts.MaxFileBytes, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError("An upload could not be written to the sources volume: {Error}", ex.GetType().Name);
            return Refused(fileName, upload.Length, SaveFailedReason);
        }

        if (received is null)
            return Refused(fileName, upload.Length, TooLargeReason(opts));

        await using (received)
            return await IntakeReceivedAsync(received, fileName, ownerUsername, origin, opts, ct);
    }

    private async Task<SourceReceipt> IntakeReceivedAsync(IncomingUpload received, string fileName, string ownerUsername, SourceOrigin origin, SourceOptions opts, CancellationToken ct)
    {
        if (received.Length == 0)
            return Refused(fileName, 0, "This file is empty.");

        if (await ReuseAsync(ownerUsername, received.Sha256, opts, ct) is { } reused)
            return reused;

        if (await QuotaReasonAsync(ownerUsername, received.Length, opts, ct) is { } quota)
            return Refused(fileName, received.Length, quota);

        var detected = SourceKindDetector.DetectFile(received.FilePath, received.Head, fileName);
        if (detected.Kind is not { } kind)
            return Refused(fileName, received.Length, detected.Reason ?? SourceKindDetector.UnsupportedReason);

        var result = await extraction.ExtractAsync(kind, received.FilePath, received.Directory, ct);
        if (result.Quality == SourceQuality.Unreadable || string.IsNullOrWhiteSpace(result.Text))
            return Refused(fileName, received.Length, result.FailureReason ?? "This file has no readable text.", kind, result.PageCount, result.Warnings);

        var warnings = result.Warnings.ToList();
        var content = kind == SourceKind.Pdf ? result.Text : result.Text.DefusePageMarkerLines();
        var text = LineWrapper.Wrap(content, opts.MaxLineChars, out var wrapped);
        if (wrapped && kind is not SourceKind.Pdf)
            warnings.Add(new SourceWarning(SourceWarningCodes.LongLinesWrapped, "Very long lines were split so they can be read."));

        var plain = PageMarkers.StripMarkers(text);
        var charCount = plain.Length;
        var budget = SourceBudgetPlanner.Classify(charCount, opts);
        AddBudgetWarning(warnings, budget, charCount, opts);

        var sourceId = Guid.NewGuid();
        string storagePath;
        try
        {
            storagePath = await files.SaveFromFileAsync(ownerUsername, sourceId, kind.StorageExtension(), received.FilePath, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogError("Source file could not be stored: {Error}", ex.GetType().Name);
            return Refused(fileName, received.Length, SaveFailedReason, kind, result.PageCount, warnings);
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
            ExtractedText = text,
            PageCount = result.PageCount,
            CharCount = charCount,
            WordCount = WordCounter.Count(plain),
            Quality = result.Quality,
            Warnings = warnings,
            Origin = origin,
            ExpiresAt = now + opts.UnattachedRetention,
            CreatedAt = now,
        };

        try
        {
            await repository.AddAsync(source, ct);
        }
        catch (SourceAlreadyStoredException)
        {
            files.TryDelete(storagePath);
            return await ReuseAsync(ownerUsername, received.Sha256, opts, ct)
                   ?? Refused(fileName, received.Length, SaveFailedReason, kind, result.PageCount, warnings);
        }
        catch
        {
            files.TryDelete(storagePath);
            throw;
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

    private async Task<string?> QuotaReasonAsync(string ownerUsername, long incomingBytes, SourceOptions opts, CancellationToken ct)
    {
        var (count, bytes) = await retention.GetUnattachedUsageAsync(ownerUsername, ct);
        if (count >= opts.MaxUnattachedSourcesPerUser)
            return string.Create(CultureInfo.InvariantCulture,
                $"You have {count:N0} files that are not used in an order yet. Remove some of them or start an order first.");
        if (bytes + incomingBytes > opts.MaxUnattachedBytesPerUser)
            return string.Create(CultureInfo.InvariantCulture,
                $"Your files that are not used in an order yet would take more than {opts.MaxUnattachedBytesPerUser / 1_048_576.0:0.#} MB. Remove some of them or start an order first.");
        return null;
    }

    public Task<SourceReceipt> IntakeTextAsync(string title, string text, string ownerUsername, SourceOrigin origin, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        var name = SourceFileNames.Sanitize(string.IsNullOrWhiteSpace(title) ? "Pasted text" : title);
        if (Path.GetExtension(name).Length == 0)
            name += ".txt";
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        return IntakeAsync(new SourceUpload(name, "text/plain", bytes.Length, new MemoryStream(bytes, writable: false)), ownerUsername, origin, ct);
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
            AddBudgetWarning(warnings, budget, source.CharCount, opts);

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
            Preview(source.ExtractedText, opts.PreviewChars),
            warnings,
            Reason: null,
            EstimatedTokens: SourceBudgetPlanner.EstimateTokens(source.CharCount, opts));
    }

    internal static string Preview(string? text, int maxChars)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        var plain = PageMarkers.StripMarkers(text).Trim();
        if (plain.Length <= maxChars)
            return plain;
        var cut = maxChars;
        if (char.IsHighSurrogate(plain[cut - 1]))
            cut--;
        return plain[..cut].TrimEnd() + "…";
    }

    private static void AddBudgetWarning(List<SourceWarning> warnings, SourceBudgetClass budget, int chars, SourceOptions opts)
    {
        if (budget == SourceBudgetClass.WithMap)
            warnings.Add(new SourceWarning(SourceWarningCodes.UsesDocumentMap, MapMessage));
        else if (budget == SourceBudgetClass.TooLarge)
            warnings.Add(new SourceWarning(SourceWarningCodes.OverBudget, string.Create(CultureInfo.InvariantCulture,
                $"This file is too long to use completely ({chars:N0} characters, the limit is {opts.MapMaxChars:N0}). Choose the pages to use.")));
    }

    private static SourceReceipt Refused(string fileName, long size, string reason, SourceKind? kind = null, int? pageCount = null, IReadOnlyList<SourceWarning>? warnings = null) =>
        new(null, fileName, kind, size, pageCount, 0, 0, SourceQuality.Unreadable, Usable: false, Duplicate: false,
            SourceBudgetClass.FullText, string.Empty, warnings ?? [], reason);

    private static string TooLargeReason(SourceOptions opts) =>
        opts.MaxFileBytes >= 1_048_576
            ? string.Create(CultureInfo.InvariantCulture, $"This file is larger than {opts.MaxFileBytes / 1_048_576.0:0.#} MB.")
            : string.Create(CultureInfo.InvariantCulture, $"This file is larger than {Math.Max(1, opts.MaxFileBytes / 1_024)} KB.");
}
