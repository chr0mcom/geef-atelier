using Geef.Atelier.Application.OAuth;
using Geef.Atelier.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Fakes.OAuth;

public static class OAuthServiceFactory
{
    public static (
        IOAuthService Service,
        InMemoryOAuthClientRepository Clients,
        InMemoryOAuthAuthorizationCodeRepository Codes,
        InMemoryOAuthAccessTokenRepository AccessTokens,
        InMemoryOAuthRefreshTokenRepository RefreshTokens,
        InMemoryOAuthAuditLogRepository AuditLog)
        Create(Action<OAuthOptions>? configureOptions = null)
    {
        var opts = new OAuthOptions();
        configureOptions?.Invoke(opts);

        var clients       = new InMemoryOAuthClientRepository();
        var codes         = new InMemoryOAuthAuthorizationCodeRepository();
        var accessTokens  = new InMemoryOAuthAccessTokenRepository();
        var refreshTokens = new InMemoryOAuthRefreshTokenRepository();
        var auditLog      = new InMemoryOAuthAuditLogRepository();

        IOAuthService service = new OAuthService(
            clients, codes, accessTokens, refreshTokens, auditLog, Options.Create(opts));

        return (service, clients, codes, accessTokens, refreshTokens, auditLog);
    }
}
