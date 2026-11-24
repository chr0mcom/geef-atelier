using Geef.Atelier.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Sources;

/// <summary>
/// One pass over the sources root: removes upload directories below <see cref="IncomingUpload.IncomingFolder"/>,
/// <c>*.part</c> files and stored files that no source refers to, each only when older than
/// <see cref="SourceOptions.OrphanFileAge"/>. Looks only at the root and the owner folders directly below it, never
/// follows links and resolves every candidate through <see cref="ISourceFileStore.Resolve"/>, so nothing outside the
/// root is touched.
/// </summary>
internal sealed class SourceOrphanScanner(ISourceFileStore files, SourceOptions options, DateTimeOffset now, ILogger logger)
{
    private const int BatchSize = 500;

    /// <summary>Runs the pass and returns how many files or directories were removed.</summary>
    public async Task<int> RunAsync(ISourceRetentionStore retention, CancellationToken ct)
    {
        var root = Path.GetFullPath(options.StorageRoot);
        if (!Directory.Exists(root) || IsLink(root))
            return 0;

        var cutoff = (now - options.OrphanFileAge).UtcDateTime;
        var removed = RemoveStaleUploads(root, cutoff);

        var candidates = new List<string>();
        foreach (var directory in SafeEnumerate(() => Directory.EnumerateDirectories(root)))
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(directory);
            if (name.StartsWith('.') || IsLink(directory))
                continue;

            foreach (var file in SafeEnumerate(() => Directory.EnumerateFiles(directory)))
            {
                if (IsLink(file) || File.GetLastWriteTimeUtc(file) >= cutoff)
                    continue;
                var relative = $"{name}/{Path.GetFileName(file)}";
                if (files.Resolve(relative) != file)
                    continue;
                if (relative.EndsWith(".part", StringComparison.Ordinal))
                {
                    removed += files.TryDelete(relative) ? 1 : 0;
                    continue;
                }

                candidates.Add(relative);
                if (candidates.Count >= BatchSize)
                    removed += await RemoveUnknownAsync(retention, candidates, ct);
            }
        }

        removed += await RemoveUnknownAsync(retention, candidates, ct);
        return removed;
    }

    private int RemoveStaleUploads(string root, DateTime cutoff)
    {
        var incoming = Path.Combine(root, IncomingUpload.IncomingFolder);
        if (!Directory.Exists(incoming) || IsLink(incoming))
            return 0;

        var removed = 0;
        foreach (var entry in SafeEnumerate(() => Directory.EnumerateFileSystemEntries(incoming)))
        {
            if (Path.GetDirectoryName(entry) != incoming || Directory.GetLastWriteTimeUtc(entry) >= cutoff)
                continue;
            try
            {
                if (IsLink(entry) || File.Exists(entry))
                    File.Delete(entry);
                else
                    Directory.Delete(entry, recursive: true);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("A left-over upload could not be removed: {Error}", ex.GetType().Name);
            }
        }

        return removed;
    }

    private async Task<int> RemoveUnknownAsync(ISourceRetentionStore retention, List<string> candidates, CancellationToken ct)
    {
        if (candidates.Count == 0)
            return 0;

        var known = await retention.FindKnownStoragePathsAsync(candidates, ct);
        var removed = candidates.Count(path => !known.Contains(path) && files.TryDelete(path));
        candidates.Clear();
        return removed;
    }

    private static bool IsLink(string path)
    {
        try
        {
            return new FileInfo(path).LinkTarget is not null || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private IEnumerable<string> SafeEnumerate(Func<IEnumerable<string>> enumerate)
    {
        try
        {
            return enumerate().ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("The sources directory could not be listed: {Error}", ex.GetType().Name);
            return [];
        }
    }
}
