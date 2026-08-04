using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Providers;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Providers;
using Geef.Atelier.Core.Scheduling;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Web.Services;

/// <summary>
/// Warms the model catalog of every active CLI provider shortly after start-up and again every night,
/// so the interactive picker is practically always a cache hit and a newly released model shows up by
/// the next morning at the latest without anyone editing a list.
/// </summary>
/// <remarks>
/// Deliberately limited to CLI providers. HTTP providers never had a cold-start problem, and warming
/// them would send long-budget requests through the shared "llm" client whose circuit breaker protects
/// real completions — a diagnostic sweep must not be able to trip it.
/// </remarks>
internal sealed class ModelCatalogWarmUpService(
    IServiceScopeFactory scopeFactory,
    IModelCatalog modelCatalog,
    IOptions<ModelCatalogOptions> options,
    ILogger<ModelCatalogWarmUpService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.WarmUpEnabled)
        {
            logger.LogInformation("Model catalog warm-up disabled.");
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, opts.WarmUpStartupDelaySeconds)), stoppingToken);
            await WarmUpAllAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = NightlyScheduleCalculator.DelayUntilNext(
                    DateTimeOffset.UtcNow, opts.WarmUpHourUtc, opts.WarmUpMinuteUtc);

                await Task.Delay(delay, stoppingToken);
                await WarmUpAllAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    /// Refreshes every active CLI provider once, sequentially. A failure for one provider is logged
    /// and the sweep continues with the next. Exposed for tests so the sweep can run without delays.
    /// </summary>
    internal async Task WarmUpAllAsync(CancellationToken ct)
    {
        IReadOnlyList<Provider> providers;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var providerService = scope.ServiceProvider.GetRequiredService<IProviderService>();
            providers = await providerService.ListAsync(includeInactive: false, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Model catalog warm-up: could not list providers.");
            return;
        }

        foreach (var provider in providers.Where(p => p.Type == ProviderType.Cli))
        {
            try
            {
                var models = await modelCatalog.WarmUpAsync(provider.Name, ct);
                logger.LogInformation("Model catalog warm-up: '{Provider}' -> {Count} models ({Source}).",
                    provider.Name, models.Count, modelCatalog.GetSource(provider.Name));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Model catalog warm-up failed for '{Provider}'.", provider.Name);
            }
        }
    }
}
