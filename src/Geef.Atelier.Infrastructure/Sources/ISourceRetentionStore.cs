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
}
