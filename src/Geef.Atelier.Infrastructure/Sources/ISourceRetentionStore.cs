using Geef.Atelier.Core.Domain.Sources;

namespace Geef.Atelier.Infrastructure.Sources;

/// <summary>Retention queries of the source cleanup that are not part of the public source repository.</summary>
internal interface ISourceRetentionStore
{
    /// <summary>
    /// Returns at most <paramref name="limit"/> sources that still have an original file, are not in the library, are
    /// linked to at least one order, and whose orders all finished before <paramref name="finishedBefore"/> without an
    /// active run.
    /// </summary>
    Task<IReadOnlyList<(Guid SourceId, string StoragePath)>> ListReleasableOriginalsAsync(DateTimeOffset finishedBefore, int limit, CancellationToken ct = default);

    /// <summary>Forgets the original file of a source; its extracted text stays.</summary>
    Task ClearStoragePathAsync(Guid sourceId, CancellationToken ct = default);

    /// <summary>Moves the expiry of a still unattached source to <paramref name="expiresAt"/> when that is later; attached sources keep no expiry.</summary>
    Task ExtendExpiryAsync(Guid sourceId, DateTimeOffset expiresAt, CancellationToken ct = default);

    /// <summary>
    /// Deletes the source only when it is still expired at <paramref name="now"/>, not in the library and not linked to an
    /// order, so an upload that just renewed its expiry keeps it.
    /// </summary>
    Task<bool> DeleteIfExpiredAndUnattachedAsync(Guid sourceId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Count, total size and total characters of the sources of <paramref name="ownerUsername"/> that are neither linked to an order nor in the library.</summary>
    Task<SourceUsage> GetUnattachedUsageAsync(string ownerUsername, CancellationToken ct = default);

    /// <summary>
    /// Stores <paramref name="source"/> unless it would take its owner beyond <paramref name="quota"/>. The usage is counted
    /// again inside the inserting transaction under a lock per owner, so parallel uploads cannot pass the quota together.
    /// Returns <c>null</c> when the source was stored, otherwise the usage that refused it. Raises
    /// <see cref="SourceAlreadyStoredException"/> when the same file of the same owner was stored at the same moment.
    /// </summary>
    Task<SourceUsage?> AddWithinQuotaAsync(SourceEntity source, SourceQuota quota, CancellationToken ct = default);

    /// <summary>Returns those of <paramref name="storagePaths"/> that a source refers to.</summary>
    Task<IReadOnlySet<string>> FindKnownStoragePathsAsync(IReadOnlyCollection<string> storagePaths, CancellationToken ct = default);
}

/// <summary>What the sources of one owner that no order uses take: files, bytes and characters of extracted text.</summary>
internal readonly record struct SourceUsage(int Count, long Bytes, long Chars);

/// <summary>Upper bounds of <see cref="SourceUsage"/> per owner.</summary>
internal readonly record struct SourceQuota(int MaxCount, long MaxBytes, long MaxChars)
{
    /// <summary>True when one more source of <paramref name="bytes"/> and <paramref name="chars"/> stays within the bounds.</summary>
    public bool Allows(SourceUsage usage, long bytes, long chars) =>
        usage.Count + 1 <= MaxCount && usage.Bytes + bytes <= MaxBytes && usage.Chars + chars <= MaxChars;
}

/// <summary>Raised when a source with the same owner and SHA-256 was stored at the same moment by another upload.</summary>
internal sealed class SourceAlreadyStoredException(Exception inner) : Exception("A source with the same content already exists for this owner.", inner);
