using System.Text.Json.Serialization;
using Geef.Atelier.Core.Domain.Sources;

namespace Geef.Atelier.Application.Sources;

/// <summary>
/// One uploaded file as received. <see cref="DeclaredContentType"/> is only a hint and never trusted. <see cref="Length"/>
/// is the size known in advance, or 0 when the content is streamed and its size is only known after reading it.
/// </summary>
public sealed record SourceUpload(string FileName, string? DeclaredContentType, long Length, Stream Content);

/// <summary>
/// Receipt for one file: what was read, whether it can be used and why not. <see cref="EstimatedTokens"/> is the
/// estimated size for the writing model. <see cref="Refusal"/> tells callers such as the upload endpoint which refusals
/// need their own answer; it is not part of the JSON.
/// </summary>
public sealed record SourceReceipt(
    Guid? SourceId,
    string FileName,
    SourceKind? Kind,
    long SizeBytes,
    int? PageCount,
    int CharCount,
    int WordCount,
    SourceQuality Quality,
    bool Usable,
    bool Duplicate,
    SourceBudgetClass BudgetClass,
    string Preview,
    IReadOnlyList<SourceWarning> Warnings,
    string? Reason,
    int EstimatedTokens = 0,
    [property: JsonIgnore] SourceRefusal Refusal = SourceRefusal.None);

/// <summary>Refusals of the intake that callers may answer differently from an unusable file.</summary>
public enum SourceRefusal
{
    /// <summary>No special refusal; <see cref="SourceReceipt.Usable"/> and <see cref="SourceReceipt.Reason"/> tell the rest.</summary>
    None = 0,

    /// <summary>The file is larger than the upload limit.</summary>
    TooLarge = 1,

    /// <summary>The person already runs as many uploads at the same time as allowed.</summary>
    TooManyUploads = 2,

    /// <summary>All upload or extraction slots stayed busy.</summary>
    Busy = 3,

    /// <summary>The upload could not be read to its end (broken or interrupted).</summary>
    Interrupted = 4,
}

/// <summary>Reads, checks and stores one file per call and answers with a receipt. Unusable files are not stored. Files are always bound to <c>ownerUsername</c>.</summary>
public interface ISourceIntakeService
{
    /// <summary>
    /// Reads and stores an uploaded file; an identical file of the same owner is reused and marked as duplicate. Takes one of
    /// the owner's upload places and an upload slot before the content is read.
    /// </summary>
    Task<SourceReceipt> IntakeAsync(SourceUpload upload, string ownerUsername, SourceOrigin origin, CancellationToken ct = default);

    /// <summary>Stores pasted text as a source; it needs no upload place or slot because the text is already in memory.</summary>
    Task<SourceReceipt> IntakeTextAsync(string title, string text, string ownerUsername, SourceOrigin origin, CancellationToken ct = default);

    /// <summary>Returns the receipt of a stored source, or <c>null</c> when it is unknown or foreign.</summary>
    Task<SourceReceipt?> GetReceiptAsync(Guid sourceId, string? requestingUsername, CancellationToken ct = default);

    /// <summary>Deletes a source and its file unless an order uses it.</summary>
    Task<SourceDeleteResult> DeleteAsync(Guid sourceId, string? requestingUsername, CancellationToken ct = default);
}

/// <summary>Outcome of <see cref="ISourceIntakeService.DeleteAsync"/>.</summary>
public enum SourceDeleteResult { Deleted = 0, NotFound = 1, InUse = 2 }
