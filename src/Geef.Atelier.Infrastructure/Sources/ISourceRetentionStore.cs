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

    /// <summary>Count and total size of the sources of <paramref name="ownerUsername"/> that are neither linked to an order nor in the library.</summary>
    Task<(int Count, long Bytes)> GetUnattachedUsageAsync(string ownerUsername, CancellationToken ct = default);

    /// <summary>Returns those of <paramref name="storagePaths"/> that a source refers to.</summary>
    Task<IReadOnlySet<string>> FindKnownStoragePathsAsync(IReadOnlyCollection<string> storagePaths, CancellationToken ct = default);
}

/// <summary>Raised when a source with the same owner and SHA-256 was stored at the same moment by another upload.</summary>
internal sealed class SourceAlreadyStoredException(Exception inner) : Exception("A source with the same content already exists for this owner.", inner);
