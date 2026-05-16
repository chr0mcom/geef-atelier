using Geef.Atelier.Application.Auth;
using Geef.Atelier.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Application.OAuth;

/// <summary>DI registration extensions for OAuth 2.1 services.</summary>
public static class ApplicationOAuthExtensions
{
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
