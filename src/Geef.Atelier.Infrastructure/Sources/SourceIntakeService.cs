using System.Globalization;
using System.Security.Cryptography;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Dashboard;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Persistence.Sources;
using Geef.Atelier.Infrastructure.Sources.Extraction;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Sources;

internal sealed class SourceIntakeService(
    ISourceRepository repository,
    ISourceRetentionStore retention,
    ISourceFileStore files,
    IEnumerable<ISourceTextExtractor> extractors,
    IOptions<SourceOptions> options,
    ILogger<SourceIntakeService> logger,
    TimeProvider? timeProvider = null) : ISourceIntakeService
{
    internal const string DuplicateMessage = "You added this file before; the earlier copy is used.";
    internal const string MapMessage = "Large file: parts will be searched, not read in full.";
    internal const string TooSlowReason = "Reading this file took too long.";
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

        var bytes = await ReadCappedAsync(upload.Content, opts.MaxFileBytes, ct);
        if (bytes is null)
            return Refused(fileName, upload.Length, TooLargeReason(opts));
        if (bytes.Length == 0)
            return Refused(fileName, 0, "This file is empty.");

        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var existing = await repository.FindByHashAsync(ownerUsername, sha, ct);
        if (existing is not null)
        {
            await retention.ExtendExpiryAsync(existing.Id, _time.GetUtcNow() + opts.UnattachedRetention, ct);
            var stored = await repository.GetAsync(existing.Id, includeText: true, ct) ?? existing;
            return ToReceipt(stored, duplicate: true);
        }

        var detected = SourceKindDetector.Detect(bytes, fileName);
        if (detected.Kind is not { } kind)
            return Refused(fileName, bytes.Length, detected.Reason ?? SourceKindDetector.UnsupportedReason);

        var extraction = await ExtractAsync(kind, bytes, opts, ct);
        if (extraction.Quality == SourceQuality.Unreadable || string.IsNullOrWhiteSpace(extraction.Text))
            return Refused(fileName, bytes.Length, extraction.FailureReason ?? "This file has no readable text.", kind, extraction.PageCount, extraction.Warnings);

        var warnings = extraction.Warnings.ToList();
        var text = LineWrapper.Wrap(extraction.Text, opts.MaxLineChars, out var wrapped);
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
            storagePath = await files.SaveAsync(ownerUsername, sourceId, kind.StorageExtension(), bytes, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogError("Source file could not be stored: {Error}", ex.GetType().Name);
            return Refused(fileName, bytes.Length, SaveFailedReason, kind, extraction.PageCount, warnings);
        }

        var now = _time.GetUtcNow();
        var source = new SourceEntity
        {
            Id = sourceId,
            OwnerUsername = ownerUsername,
            FileName = fileName,
            ContentType = kind.ContentType(),
            Kind = kind,
            SizeBytes = bytes.Length,
            Sha256 = sha,
            StoragePath = storagePath,
            ExtractedText = text,
            PageCount = extraction.PageCount,
            CharCount = charCount,
            WordCount = WordCounter.Count(plain),
            Quality = extraction.Quality,
            Warnings = warnings,
            Origin = origin,
            ExpiresAt = now + opts.UnattachedRetention,
            CreatedAt = now,
        };

        try
        {
            await repository.AddAsync(source, ct);
        }
        catch
        {
            files.TryDelete(storagePath);
            throw;
        }

        return ToReceipt(source, duplicate: false);
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

    private async Task<SourceExtraction> ExtractAsync(SourceKind kind, byte[] bytes, SourceOptions opts, CancellationToken ct)
    {
        var extractor = extractors.FirstOrDefault(e => e.Kind == kind)
                        ?? extractors.First(e => e.Kind == SourceKind.Text);
        try
        {
            return await extractor.ExtractAsync(bytes, ct).WaitAsync(opts.ExtractionTimeout, ct);
        }
        catch (TimeoutException)
        {
            logger.LogWarning("Extraction of a {Kind} file took longer than {Timeout}.", kind, opts.ExtractionTimeout);
            return SourceExtraction.Unreadable(TooSlowReason);
        }
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

    private static async Task<byte[]?> ReadCappedAsync(Stream content, long maxBytes, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes)
                return null;
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
