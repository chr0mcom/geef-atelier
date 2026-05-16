using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Persistence.OAuth;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Web.Services;

internal sealed class OAuthCleanupBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<OAuthOptions> options,
    ILogger<OAuthCleanupBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, options.Value.CleanupIntervalMinutes));
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var authCodeRepo     = scope.ServiceProvider.GetRequiredService<IOAuthAuthorizationCodeRepository>();
                var accessTokenRepo  = scope.ServiceProvider.GetRequiredService<IOAuthAccessTokenRepository>();
                var refreshTokenRepo = scope.ServiceProvider.GetRequiredService<IOAuthRefreshTokenRepository>();

                await authCodeRepo.DeleteExpiredAsync(stoppingToken);
                await accessTokenRepo.DeleteExpiredAsync(stoppingToken);
                await refreshTokenRepo.DeleteExpiredAsync(stoppingToken);

                logger.LogDebug("OAuth cleanup completed");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "OAuth cleanup failed");
            }
        }
    }
}
