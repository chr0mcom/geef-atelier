using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Geef.Atelier.Web.Health;

/// <summary>Reports Unhealthy while the database has migrations that are not applied yet.</summary>
public sealed class PendingMigrationsHealthCheck(AtelierDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            return pending.Count == 0
                ? HealthCheckResult.Healthy("All migrations are applied.")
                : HealthCheckResult.Unhealthy($"{pending.Count} pending migration(s): {string.Join(", ", pending)}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Pending migrations could not be determined.", ex);
        }
    }
}
