using Geef.Atelier.Core.Domain.OAuth;

namespace Geef.Atelier.Application.OAuth;

/// <summary>
/// Self-hosted OAuth 2.1 Authorization Server for the MCP endpoint (D-041).
/// Implements RFC 8414/7591/7636/7009/8252: dynamic client registration,
/// authorization-code flow with mandatory PKCE/S256, opaque tokens stored only as
/// SHA-256 hash, refresh-token rotation with reuse-detection, and an audit trail.
/// All token comparisons are constant-time; tokens are never logged.
/// </summary>
public interface IOAuthService
{
    /// <summary>Registers a new public client (RFC 7591). Generates a client id when none is supplied and validates the redirect URIs.</summary>
    Task<ClientRegistrationResult> RegisterClientAsync(ClientRegistrationRequest request, CancellationToken ct);

    /// <summary>Validates an incoming authorization request (response_type=code, registered client, exact redirect-URI match incl. RFC 8252 loopback rule, S256 PKCE) before the consent page is shown.</summary>
    Task<AuthorizationValidationResult> ValidateAuthorizationRequestAsync(AuthorizationRequest request, CancellationToken ct);

    /// <summary>Issues a single-use authorization code (10-minute lifetime) bound to client, redirect URI and PKCE challenge after the user approved consent. <paramref name="userId"/> is the authorizing username and becomes the run-attribution subject.</summary>
    Task<string> CreateAuthorizationCodeAsync(string clientId, string userId, string redirectUri, string scope, string codeChallenge, string codeChallengeMethod, CancellationToken ct);

    /// <summary>Exchanges an authorization code for an access/refresh token pair. Verifies the PKCE code_verifier and enforces single-use of the code.</summary>
    Task<TokenResponse> ExchangeAuthorizationCodeAsync(string code, string clientId, string redirectUri, string codeVerifier, CancellationToken ct);

    /// <summary>Rotates a refresh token, returning a new pair. Re-use of an already-consumed refresh token is treated as theft (RFC 6819) and revokes all of the user's tokens.</summary>
    Task<TokenResponse> RefreshTokenAsync(string refreshToken, string clientId, CancellationToken ct);

    /// <summary>Validates an opaque access token via DB hash lookup; returns the bound user and scope, or an invalid result for unknown/expired/revoked tokens.</summary>
    Task<TokenValidationResult> ValidateAccessTokenAsync(string accessToken, CancellationToken ct);

    /// <summary>Revokes a single access or refresh token (RFC 7009). Idempotent — unknown tokens are accepted silently.</summary>
    Task RevokeTokenAsync(string token, string clientId, CancellationToken ct);

    /// <summary>Revokes every active access and refresh token of a user (used by refresh-reuse detection and on user deactivation/deletion).</summary>
    Task RevokeAllUserTokensAsync(string userId, CancellationToken ct);

    /// <summary>Revokes all tokens a single client holds for the given user (used by the "connected clients" self-service UI).</summary>
    Task RevokeClientAsync(string userId, string clientId, CancellationToken ct);

    /// <summary>Appends an entry to the permanent OAuth audit log (kept beyond token cleanup for forensics).</summary>
    Task LogEventAsync(OAuthAuditLogEntry entry, CancellationToken ct);

    /// <summary>Returns the user's currently active (non-expired, non-revoked) access tokens.</summary>
    Task<IReadOnlyList<OAuthAccessToken>> GetActiveTokensForUserAsync(string userId, CancellationToken ct);

    /// <summary>Returns the distinct clients the user has connected, with last-used info, for the self-service UI.</summary>
    Task<IReadOnlyList<ConnectedClientInfo>> GetConnectedClientsAsync(string userId, CancellationToken ct);

    /// <summary>Returns all registered OAuth clients (admin client-management UI).</summary>
    Task<IReadOnlyList<OAuthClient>> GetAllClientsAsync(CancellationToken ct);

    /// <summary>Deletes a registered client and cascades to its codes/tokens (admin action).</summary>
    Task DeleteClientAsync(string clientId, CancellationToken ct);
}
