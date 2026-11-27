using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Persistence;

/// <summary>Startup migration that fails loudly.</summary>
public static class DatabaseStartupExtensions
{
    /// <summary>
    /// Applies all pending migrations. A failure is logged and rethrown so the host does not start on an outdated
    /// schema; a half-migrated database must never serve runs. A database that does not start from the baseline of this
    /// version (<see cref="EnsureStartsFromBaselineAsync"/>) is refused before anything is written to it.
    /// </summary>
    public static async Task MigrateDatabaseOrFailAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Geef.Atelier.Startup");
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<AtelierDbContext>();
            await db.EnsureStartsFromBaselineAsync(ct);
            await db.Database.MigrateAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Database migration failed on startup; the application will not start.");
            throw;
        }
    }

    /// <summary>
    /// Throws <see cref="IncompatibleDatabaseException"/> for a database that migrations of this version must not touch: one
    /// whose migration history lacks the first migration of this version (it was created by an earlier version and has not
    /// been converted), or one that has tables but no migration history. It only reads. A missing or empty database passes,
    /// and so does a database that has the baseline plus migrations this version does not know (an older image after an
    /// additive upgrade).
    /// </summary>
    public static async Task EnsureStartsFromBaselineAsync(this AtelierDbContext db, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (!await db.GetService<IRelationalDatabaseCreator>().ExistsAsync(ct))
            return;

        var baseline = db.Database.GetMigrations().First();
        var history = db.GetService<IHistoryRepository>();
        IReadOnlyList<string> applied = await history.ExistsAsync(ct)
            ? (await history.GetAppliedMigrationsAsync(ct)).Select(row => row.MigrationId).ToList()
            : [];
        if (applied.Contains(baseline, StringComparer.Ordinal))
            return;
        if (applied.Count > 0)
            throw new IncompatibleDatabaseException(
                $"The database was created by an earlier version of Geef.Atelier and has not been converted: its migration history has {applied.Count} entries but not '{baseline}'. " +
                "Nothing was changed. Convert it first (take a backup before that; see decision D-092) or start the previous version.");

        var tables = await db.Database
            .SqlQuery<int>($"""
                SELECT count(*)::int AS "Value" FROM information_schema.tables
                WHERE table_schema = 'public' AND table_type = 'BASE TABLE' AND table_name <> '__EFMigrationsHistory'
                """)
            .SingleAsync(ct);
        if (tables > 0)
            throw new IncompatibleDatabaseException(
                $"The database has {tables} tables but no migration history, so its schema is unknown. Nothing was changed. " +
                "Point the application at an empty database or at one that was converted for this version.");
    }
}

/// <summary>The database does not start from the migration baseline of this version; the application does not start on it.</summary>
public sealed class IncompatibleDatabaseException(string message) : InvalidOperationException(message);
