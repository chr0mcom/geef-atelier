using Geef.Atelier.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Application.Auth;

/// <summary>DI registration extensions for auth services.</summary>
public static class ApplicationAuthExtensions
{
    /// <summary>Registers authentication services and binds <see cref="AtelierUserOptions"/> from configuration.</summary>
    public static IServiceCollection AddAtelierAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AtelierUserOptions>(opts =>
        {
            configuration.GetSection(AtelierUserOptions.SectionName).Bind(opts);
            // Also accept short env var names (ATELIER_USER / ATELIER_PASSWORD_HASH) as fallback
            // so docker-compose users don't need the double-underscore ASP.NET Core convention.
            if (string.IsNullOrEmpty(opts.Username))
                opts.Username = Environment.GetEnvironmentVariable("ATELIER_USER") ?? "";
            if (string.IsNullOrEmpty(opts.PasswordHash))
                opts.PasswordHash = Environment.GetEnvironmentVariable("ATELIER_PASSWORD_HASH") ?? "";
        });
        services.AddScoped<IUserAuthenticator, AtelierUserAuthenticator>();
        return services;
    }
}
