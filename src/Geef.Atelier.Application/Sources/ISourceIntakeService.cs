using Geef.Atelier.Core.Domain.Sources;

namespace Geef.Atelier.Application.Sources;

/// <summary>One uploaded file as received. <see cref="DeclaredContentType"/> is only a hint and never trusted.</summary>
public sealed record SourceUpload(string FileName, string? DeclaredContentType, long Length, Stream Content);

/// <summary>Receipt for one file: what was read, whether it can be used and why not. <see cref="EstimatedTokens"/> is the estimated size for the writing model.</summary>
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
    int EstimatedTokens = 0);

/// <summary>Reads, checks and stores one file per call and answers with a receipt. Unusable files are not stored. Files are always bound to <c>ownerUsername</c>.</summary>
public interface ISourceIntakeService
{
    /// <summary>Reads and stores an uploaded file; an identical file of the same owner is reused and marked as duplicate.</summary>
    Task<SourceReceipt> IntakeAsync(SourceUpload upload, string ownerUsername, SourceOrigin origin, CancellationToken ct = default);

    /// <summary>Stores pasted text as a source.</summary>
    Task<SourceReceipt> IntakeTextAsync(string title, string text, string ownerUsername, SourceOrigin origin, CancellationToken ct = default);

    /// <summary>Returns the receipt of a stored source, or <c>null</c> when it is unknown or foreign.</summary>
    Task<SourceReceipt?> GetReceiptAsync(Guid sourceId, string? requestingUsername, CancellationToken ct = default);

    /// <summary>Deletes a source and its file unless an order uses it.</summary>
    Task<SourceDeleteResult> DeleteAsync(Guid sourceId, string? requestingUsername, CancellationToken ct = default);
}

/// <summary>Outcome of <see cref="ISourceIntakeService.DeleteAsync"/>.</summary>
public enum SourceDeleteResult { Deleted = 0, NotFound = 1, InUse = 2 }
