using Geef.Atelier.Application.OAuth;
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
            if (string.IsNullOrEmpty(opts.Username))
                opts.Username = Environment.GetEnvironmentVariable("ATELIER_USER") ?? "";
            if (string.IsNullOrEmpty(opts.PasswordHash))
                opts.PasswordHash = Environment.GetEnvironmentVariable("ATELIER_PASSWORD_HASH") ?? "";
        });
        services.AddScoped<IUserAuthenticator, AtelierUserAuthenticator>();
        return services;
    }

    /// <summary>Registers MCP token authentication services and binds <see cref="AtelierMcpOptions"/> from configuration.</summary>
    public static IServiceCollection AddAtelierMcpAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AtelierMcpOptions>(opts =>
        {
            configuration.GetSection(AtelierMcpOptions.SectionName).Bind(opts);
            if (string.IsNullOrEmpty(opts.Token))
                opts.Token = Environment.GetEnvironmentVariable("ATELIER_MCP_TOKEN") ?? "";
        });
        services.AddScoped<StaticTokenValidator>();
        return services;
    }

    /// <summary>Registers OAuth 2.1 services and binds <see cref="OAuthOptions"/> from configuration.</summary>
    public static IServiceCollection AddAtelierOAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OAuthOptions>(opts =>
        {
            configuration.GetSection(OAuthOptions.SectionName).Bind(opts);
            if (string.IsNullOrEmpty(opts.Issuer))
                opts.Issuer = Environment.GetEnvironmentVariable("ATELIER_OAUTH_ISSUER") ?? "https://geef.stefan-bechtel.de";
            if (string.IsNullOrEmpty(opts.RegistrationToken))
                opts.RegistrationToken = Environment.GetEnvironmentVariable("OAUTH_REGISTRATION_TOKEN") ?? "";
        });
        services.AddScoped<IOAuthService, OAuthService>();
        services.AddScoped<OAuthAccessTokenValidator>();
        services.AddScoped<ITokenValidator, CompositeTokenValidator>();
        return services;
    }
}
