using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Persistence.Crew;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Web.Services;

/// <summary>
/// Periodically archives custom General/DomainScoped specialization packs that have been unused for
/// longer than the retention window and are not referenced by any crew template. System packs and
/// TaskBound packs are never touched here (TaskBound packs are cascade-deleted with their crew).
/// </summary>
internal sealed class PackArchivalBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<PackGcOptions> options,
    ILogger<PackArchivalBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled)
        {
            logger.LogInformation("Pack auto-archival disabled.");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, opts.IntervalHours));
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var packRepo     = scope.ServiceProvider.GetRequiredService<ISpecializationPackRepository>();
                var templateRepo = scope.ServiceProvider.GetRequiredService<ICrewTemplateRepository>();

                var templates = await templateRepo.ListAsync(includeSystem: true, stoppingToken);
                var referenced = templates
                    .SelectMany(t => t.ActorPackBindings.Values.SelectMany(v => v))
                    .ToHashSet(StringComparer.Ordinal);

                var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, opts.RetentionDays));
                var archived = await packRepo.ArchiveUnusedAsync(cutoff, referenced, stoppingToken);

                if (archived.Count > 0)
                    logger.LogInformation("Pack auto-archival: archived {Count} unused pack(s): {Names}",
                        archived.Count, string.Join(", ", archived));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Pack auto-archival failed");
            }
        }
    }
}
