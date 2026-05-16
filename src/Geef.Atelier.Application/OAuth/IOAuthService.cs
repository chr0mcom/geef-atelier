using Geef.Atelier.Core.Domain.OAuth;

namespace Geef.Atelier.Application.OAuth;

public interface IOAuthService
{
    Task<ClientRegistrationResult> RegisterClientAsync(ClientRegistrationRequest request, CancellationToken ct);
    Task<AuthorizationValidationResult> ValidateAuthorizationRequestAsync(AuthorizationRequest request, CancellationToken ct);
    Task<string> CreateAuthorizationCodeAsync(string clientId, string userId, string redirectUri, string scope, string codeChallenge, string codeChallengeMethod, CancellationToken ct);
    Task<TokenResponse> ExchangeAuthorizationCodeAsync(string code, string clientId, string redirectUri, string codeVerifier, CancellationToken ct);
    Task<TokenResponse> RefreshTokenAsync(string refreshToken, string clientId, CancellationToken ct);
    Task<TokenValidationResult> ValidateAccessTokenAsync(string accessToken, CancellationToken ct);
    Task RevokeTokenAsync(string token, string clientId, CancellationToken ct);
    Task RevokeAllUserTokensAsync(string userId, CancellationToken ct);
    Task RevokeClientAsync(string userId, string clientId, CancellationToken ct);
    Task LogEventAsync(OAuthAuditLogEntry entry, CancellationToken ct);
    Task<IReadOnlyList<OAuthAccessToken>> GetActiveTokensForUserAsync(string userId, CancellationToken ct);
    Task<IReadOnlyList<ConnectedClientInfo>> GetConnectedClientsAsync(string userId, CancellationToken ct);
}
