using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Persistence;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Web.Auth;

/// <summary>Creates or synchronises the administrator account configured in <see cref="AtelierUserOptions"/>.</summary>
public static class AdminUserSeedingExtensions
{
    /// <summary>
    /// Adds the configured administrator (<c>ATELIER_USER</c> / <c>ATELIER_PASSWORD_HASH</c>) to the user table, or
    /// restores password hash, admin role and active flag of an existing account. Does nothing without configuration;
    /// failures are logged, never thrown, so a broken seed never stops the application.
    /// </summary>
    public static async Task SeedAdminUserFromOptionsAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Geef.Atelier.Web.AdminUserSeeding");
        var options = scope.ServiceProvider.GetRequiredService<IOptions<AtelierUserOptions>>().Value;
        if (string.IsNullOrEmpty(options.Username) || string.IsNullOrEmpty(options.PasswordHash))
            return;

        var users = scope.ServiceProvider.GetRequiredService<IAtelierUserRepository>();
        try
        {
            var existing = await users.FindByUsernameAsync(options.Username, ct);
            if (existing is null)
            {
                var now = DateTimeOffset.UtcNow;
                await users.AddAsync(new AtelierUser(
                    UserId: Guid.NewGuid().ToString(),
                    Username: options.Username,
                    PasswordHash: options.PasswordHash,
                    Email: null,
                    IsActive: true,
                    IsAdmin: true,
                    CreatedAt: now,
                    UpdatedAt: now), ct);
                logger.LogInformation("Admin user '{Username}' seeded from environment variables", options.Username);
            }
            else if (!existing.IsAdmin || existing.PasswordHash != options.PasswordHash)
            {
                await users.UpdateAsync(existing with
                {
                    PasswordHash = options.PasswordHash,
                    IsAdmin = true,
                    IsActive = true,
                    UpdatedAt = DateTimeOffset.UtcNow,
                }, ct);
                logger.LogInformation("Admin user '{Username}' synchronized from environment variables", options.Username);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to seed admin user from environment variables");
        }
    }
}
