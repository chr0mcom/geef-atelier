using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.OAuth;
using Geef.Atelier.Core.Persistence.OAuth;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Application.OAuth;

internal sealed class OAuthService(
    IOAuthClientRepository clientRepo,
    IOAuthAuthorizationCodeRepository codeRepo,
    IOAuthAccessTokenRepository accessTokenRepo,
    IOAuthRefreshTokenRepository refreshTokenRepo,
    IOAuthAuditLogRepository auditLogRepo,
    IOptions<OAuthOptions> oauthOptions) : IOAuthService
{
    private OAuthOptions Opts => oauthOptions.Value;

    public async Task<ClientRegistrationResult> RegisterClientAsync(ClientRegistrationRequest request, CancellationToken ct)
    {
        var now      = DateTimeOffset.UtcNow;
        var clientId = string.IsNullOrWhiteSpace(request.ClientId)
            ? OAuthCrypto.GenerateToken()
            : request.ClientId.Trim();
        var client   = new OAuthClient(
            ClientId: clientId,
            ClientName: request.ClientName,
            RedirectUris: request.RedirectUris,
            ClientSecretHash: null,
            LogoUri: request.LogoUri,
            ClientUri: request.ClientUri,
            IsPublic: true,
            CreatedAt: now,
            UpdatedAt: now);

        await clientRepo.AddAsync(client, ct);
        await auditLogRepo.AddAsync(new OAuthAuditLogEntry(
            Id: Guid.NewGuid(),
            EventType: "ClientRegistered",
            ClientId: clientId,
            UserId: null,
            IpAddress: null,
            UserAgent: null,
            EventDataJson: null,
            CreatedAt: now), ct);

        return new ClientRegistrationResult(clientId, now);
    }

    public async Task<AuthorizationValidationResult> ValidateAuthorizationRequestAsync(AuthorizationRequest request, CancellationToken ct)
    {
        if (!string.Equals(request.ResponseType, "code", StringComparison.Ordinal))
            return new AuthorizationValidationResult(false, "unsupported_response_type", "Only code response_type is supported", null);

        var client = await clientRepo.FindByClientIdAsync(request.ClientId, ct);
        if (client is null)
            return new AuthorizationValidationResult(false, "invalid_client", "Unknown client_id", null);

        if (string.IsNullOrEmpty(request.CodeChallenge))
            return new AuthorizationValidationResult(false, "invalid_request", "PKCE required", null);

        if (!string.Equals(request.CodeChallengeMethod, "S256", StringComparison.Ordinal))
            return new AuthorizationValidationResult(false, "invalid_request", "Only S256 code_challenge_method is supported", null);

        if (!RedirectUriMatches(client.RedirectUris, request.RedirectUri))
            return new AuthorizationValidationResult(false, "invalid_request", "redirect_uri does not match registered URIs", null);

        return new AuthorizationValidationResult(true, null, null, client);
    }

    public async Task<string> CreateAuthorizationCodeAsync(
        string clientId, string userId, string redirectUri,
        string scope, string codeChallenge, string codeChallengeMethod,
        CancellationToken ct)
    {
        var plainCode = OAuthCrypto.GenerateToken();
        var codeHash  = OAuthCrypto.HashToken(plainCode);
        var now       = DateTimeOffset.UtcNow;

        var code = new OAuthAuthorizationCode(
            CodeHash: codeHash,
            ClientId: clientId,
            UserId: userId,
            RedirectUri: redirectUri,
            Scope: scope,
            CodeChallenge: codeChallenge,
            CodeChallengeMethod: codeChallengeMethod,
            ExpiresAt: now.AddMinutes(Opts.AuthorizationCodeTtlMinutes),
            UsedAt: null,
            CreatedAt: now);

        await codeRepo.AddAsync(code, ct);
        return plainCode;
    }

    public async Task<TokenResponse> ExchangeAuthorizationCodeAsync(
        string code, string clientId, string redirectUri, string codeVerifier,
        CancellationToken ct)
    {
        var codeHash = OAuthCrypto.HashToken(code);

        // Validate client_id ownership BEFORE consuming — mirrors the RefreshTokenAsync pattern
        // to prevent a wrong-client request from irreversibly burning a valid authorization code.
        var lookup = await codeRepo.FindByCodeHashAsync(codeHash, ct);
        if (lookup is not null && !ClientOwns(lookup.ClientId, clientId))
            throw new InvalidOperationException("client_id mismatch");

        var consumed = await codeRepo.ConsumeAsync(codeHash, ct);

        if (consumed is null)
            throw new InvalidOperationException("Authorization code not found or already used");

        if (consumed.ExpiresAt < DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Authorization code expired");

        if (!string.Equals(consumed.RedirectUri, redirectUri, StringComparison.Ordinal))
            throw new InvalidOperationException("redirect_uri mismatch");

        if (!OAuthCrypto.VerifyPkceS256(codeVerifier, consumed.CodeChallenge))
            throw new InvalidOperationException("PKCE verification failed");

        return await IssueTokenPairAsync(consumed.ClientId, consumed.UserId, consumed.Scope, ct);
    }

    public async Task<TokenResponse> RefreshTokenAsync(string refreshToken, string clientId, CancellationToken ct)
    {
        var tokenHash = OAuthCrypto.HashToken(refreshToken);

        // Validate client_id ownership BEFORE consuming so a wrong-client request cannot
        // burn a valid token and trigger false reuse-detection for the legitimate owner.
        var lookup = await refreshTokenRepo.FindByHashAsync(tokenHash, ct);
        if (lookup is not null && !ClientOwns(lookup.ClientId, clientId))
            throw new InvalidOperationException("client_id mismatch");

        var consumed = await refreshTokenRepo.ConsumeAsync(tokenHash, ct);

        if (consumed is null)
        {
            var reused = await refreshTokenRepo.FindByHashAsync(tokenHash, ct);
            if (reused is not null)
                await RevokeAllUserTokensAsync(reused.UserId, ct);
            throw new InvalidOperationException("Invalid refresh token");
        }

        if (consumed.ExpiresAt < DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Refresh token expired");

        var response = await IssueTokenPairAsync(consumed.ClientId, consumed.UserId, consumed.Scope, ct);

        await auditLogRepo.AddAsync(new OAuthAuditLogEntry(
            Id: Guid.NewGuid(),
            EventType: "TokenRefreshed",
            ClientId: clientId,
            UserId: consumed.UserId,
            IpAddress: null,
            UserAgent: null,
            EventDataJson: null,
            CreatedAt: DateTimeOffset.UtcNow), ct);

        return response;
    }

    public async Task<TokenValidationResult> ValidateAccessTokenAsync(string accessToken, CancellationToken ct)
    {
        var tokenHash = OAuthCrypto.HashToken(accessToken);
        var token     = await accessTokenRepo.FindByHashAsync(tokenHash, ct);

        if (token is null || token.RevokedAt is not null || token.ExpiresAt < DateTimeOffset.UtcNow)
            return new TokenValidationResult(false, null, null, null, null);

        return new TokenValidationResult(true, token.UserId, token.ClientId, token.Scope, token.ExpiresAt);
    }

    public async Task RevokeTokenAsync(string token, string clientId, CancellationToken ct)
    {
        var tokenHash   = OAuthCrypto.HashToken(token);
        var accessToken = await accessTokenRepo.FindByHashAsync(tokenHash, ct);

        if (accessToken is not null)
        {
            if (!ClientOwns(accessToken.ClientId, clientId)) return; // RFC 7009 §2.1: silent no-op on mismatch

            var now = DateTimeOffset.UtcNow;
            await accessTokenRepo.RevokeByHashAsync(tokenHash, ct);
            await auditLogRepo.AddAsync(new OAuthAuditLogEntry(
                Id: Guid.NewGuid(),
                EventType: "TokenRevoked",
                ClientId: clientId,
                UserId: accessToken.UserId,
                IpAddress: null,
                UserAgent: null,
                EventDataJson: null,
                CreatedAt: now), ct);
            return;
        }

        var refreshToken = await refreshTokenRepo.FindByHashAsync(tokenHash, ct);
        if (refreshToken is not null)
        {
            if (!ClientOwns(refreshToken.ClientId, clientId)) return; // RFC 7009 §2.1: silent no-op on mismatch

            var now = DateTimeOffset.UtcNow;
            await refreshTokenRepo.RevokeByHashAsync(tokenHash, ct);
            await auditLogRepo.AddAsync(new OAuthAuditLogEntry(
                Id: Guid.NewGuid(),
                EventType: "TokenRevoked",
                ClientId: clientId,
                UserId: refreshToken.UserId,
                IpAddress: null,
                UserAgent: null,
                EventDataJson: null,
                CreatedAt: now), ct);
        }
    }

    public async Task RevokeAllUserTokensAsync(string userId, CancellationToken ct)
    {
        await accessTokenRepo.RevokeByUserIdAsync(userId, ct);
        await refreshTokenRepo.RevokeByUserIdAsync(userId, ct);
    }

    public async Task RevokeClientAsync(string userId, string clientId, CancellationToken ct)
    {
        await accessTokenRepo.RevokeByClientIdAndUserIdAsync(clientId, userId, ct);
        await refreshTokenRepo.RevokeByClientIdAndUserIdAsync(clientId, userId, ct);
        await auditLogRepo.AddAsync(new OAuthAuditLogEntry(
            Id: Guid.NewGuid(),
            EventType: "ClientRevoked",
            ClientId: clientId,
            UserId: userId,
            IpAddress: null,
            UserAgent: null,
            EventDataJson: null,
            CreatedAt: DateTimeOffset.UtcNow), ct);
    }

    public async Task LogEventAsync(OAuthAuditLogEntry entry, CancellationToken ct)
        => await auditLogRepo.AddAsync(entry, ct);

    public Task<IReadOnlyList<OAuthAccessToken>> GetActiveTokensForUserAsync(string userId, CancellationToken ct)
        => accessTokenRepo.GetActiveTokensByUserIdAsync(userId, ct);

    public async Task<IReadOnlyList<ConnectedClientInfo>> GetConnectedClientsAsync(string userId, CancellationToken ct)
    {
        var tokens = await accessTokenRepo.GetActiveTokensByUserIdAsync(userId, ct);
        var result = new List<ConnectedClientInfo>(tokens.Count);
        foreach (var token in tokens)
        {
            var client = await clientRepo.FindByClientIdAsync(token.ClientId, ct);
            result.Add(new ConnectedClientInfo(
                ClientId:   token.ClientId,
                ClientName: client?.ClientName ?? token.ClientId,
                Scope:      token.Scope,
                IssuedAt:   token.CreatedAt));
        }
        return result;
    }

    public Task<IReadOnlyList<OAuthClient>> GetAllClientsAsync(CancellationToken ct)
        => clientRepo.GetAllAsync(ct);

    public Task DeleteClientAsync(string clientId, CancellationToken ct)
        => clientRepo.DeleteAsync(clientId, ct);

    private static bool ClientOwns(string storedClientId, string requestedClientId)
        => string.Equals(storedClientId, requestedClientId, StringComparison.Ordinal);

    private async Task<TokenResponse> IssueTokenPairAsync(string clientId, string userId, string scope, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var plainAccessToken = OAuthCrypto.GenerateToken();
        var accessTokenHash  = OAuthCrypto.HashToken(plainAccessToken);
        var accessExpiresAt  = now.AddHours(Opts.AccessTokenTtlHours);

        await accessTokenRepo.AddAsync(new OAuthAccessToken(
            TokenHash: accessTokenHash,
            ClientId: clientId,
            UserId: userId,
            Scope: scope,
            ExpiresAt: accessExpiresAt,
            RevokedAt: null,
            CreatedAt: now), ct);

        var plainRefreshToken = OAuthCrypto.GenerateToken();
        var refreshTokenHash  = OAuthCrypto.HashToken(plainRefreshToken);

        await refreshTokenRepo.AddAsync(new OAuthRefreshToken(
            TokenHash: refreshTokenHash,
            ClientId: clientId,
            UserId: userId,
            Scope: scope,
            ExpiresAt: now.AddDays(Opts.RefreshTokenTtlDays),
            UsedAt: null,
            RevokedAt: null,
            CreatedAt: now), ct);

        return new TokenResponse(
            AccessToken: plainAccessToken,
            TokenType: "Bearer",
            ExpiresIn: Opts.AccessTokenTtlHours * 3600,
            RefreshToken: plainRefreshToken,
            Scope: scope);
    }

    private static bool RedirectUriMatches(IReadOnlyList<string> registered, string requested)
    {
        foreach (var reg in registered)
        {
            if (string.Equals(reg, requested, StringComparison.Ordinal)) return true;
            if (IsLoopbackUri(reg) && IsLoopbackMatch(reg, requested)) return true;
        }
        return false;
    }

    private static bool IsLoopbackUri(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u)) return false;
        return u.Host is "127.0.0.1";
    }

    private static bool IsLoopbackMatch(string registered, string requested)
    {
        if (!Uri.TryCreate(registered, UriKind.Absolute, out var reg)) return false;
        if (!Uri.TryCreate(requested, UriKind.Absolute, out var req)) return false;
        return reg.Scheme == req.Scheme &&
               reg.Host   == req.Host &&
               reg.AbsolutePath == req.AbsolutePath;
    }
}
