using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Persistence.Sources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Sources;

/// <summary>
/// Deletes sources that were never attached to an order once they expired (row and file), frees the original files
/// of sources whose orders all finished more than <see cref="SourceOptions.OriginalRetention"/> ago (the text stays), and
/// removes files in the sources directory that no source refers to once they are older than
/// <see cref="SourceOptions.OrphanFileAge"/> (left-over uploads, half-written files).
/// </summary>
internal sealed class SourceCleanupBackgroundService(
    IServiceScopeFactory scopeFactory,
    ISourceFileStore files,
    IOptions<SourceOptions> options,
    ILogger<SourceCleanupBackgroundService> logger,
    TimeProvider? timeProvider = null) : BackgroundService
{
    private const int BatchSize = 200;
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.CleanupInterval > TimeSpan.Zero ? options.Value.CleanupInterval : TimeSpan.FromHours(1);
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Source cleanup failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Runs one cleanup pass and returns how many sources were deleted and how many originals were freed.</summary>
    internal async Task<(int Deleted, int OriginalsFreed)> RunOnceAsync(CancellationToken ct)
    {
        var result = await CleanSourcesAsync(ct);
        await RemoveOrphansAsync(ct);
        return result;
    }

    /// <summary>Removes orphaned files and left-over upload directories below the sources root; returns how many entries were removed.</summary>
    internal async Task<int> RemoveOrphansAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var retention = scope.ServiceProvider.GetRequiredService<ISourceRetentionStore>();
        var removed = await new SourceOrphanScanner(files, options.Value, _time.GetUtcNow(), logger).RunAsync(retention, ct);
        if (removed > 0)
            logger.LogInformation("Source cleanup removed {Removed} orphaned files.", removed);
        return removed;
    }

    private async Task<(int Deleted, int OriginalsFreed)> CleanSourcesAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISourceRepository>();
        var retention = scope.ServiceProvider.GetRequiredService<ISourceRetentionStore>();
        var now = _time.GetUtcNow();

        var deleted = 0;
        foreach (var source in await repository.ListExpiredUnattachedAsync(now, BatchSize, ct))
        {
            if (!await retention.DeleteIfExpiredAndUnattachedAsync(source.Id, now, ct))
                continue;
            deleted++;
            if (source.StoragePath is { } path)
                files.TryDelete(path);
        }

        var freed = 0;
        foreach (var (sourceId, path) in await retention.ListReleasableOriginalsAsync(now - options.Value.OriginalRetention, BatchSize, ct))
        {
            files.TryDelete(path);
            await retention.ClearStoragePathAsync(sourceId, ct);
            freed++;
        }

        if (deleted + freed > 0)
            logger.LogInformation("Source cleanup deleted {Deleted} unattached sources and freed {Freed} original files.", deleted, freed);
        return (deleted, freed);
    }
}
