using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Persistence;

/// <summary>Startup migration that fails loudly.</summary>
public static class DatabaseStartupExtensions
{
    /// <summary>
    /// Applies all pending migrations. A failure is logged and rethrown so the host does not start on an outdated
    /// schema; a half-migrated database must never serve runs.
    /// </summary>
    public static async Task MigrateDatabaseOrFailAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Geef.Atelier.Startup");
        try
        {
            await scope.ServiceProvider.GetRequiredService<AtelierDbContext>().Database.MigrateAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Database migration failed on startup; the application will not start.");
            throw;
        }
    }
}
