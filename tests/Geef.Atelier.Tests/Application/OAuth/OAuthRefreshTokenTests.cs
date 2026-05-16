using Geef.Atelier.Application.OAuth;
using Geef.Atelier.Tests.Fakes.OAuth;

namespace Geef.Atelier.Tests.Application.OAuth;

public sealed class OAuthRefreshTokenTests
{
    private const string Verifier  = "MyVerifier1234567890123456789012345678901234";
    private static string Challenge => OAuthCrypto.Sha256Base64Url(Verifier);

    private static async Task<(IOAuthService svc, InMemoryOAuthRefreshTokenRepository refreshTokens,
        InMemoryOAuthAccessTokenRepository accessTokens, string clientId, string refreshToken)> PrepareAsync()
    {
        var (svc, _, _, accessTokens, refreshTokens, _) = OAuthServiceFactory.Create();
        var reg = await svc.RegisterClientAsync(
            new Geef.Atelier.Application.OAuth.ClientRegistrationRequest(
                "App", ["https://example.com/callback"], null, null),
            CancellationToken.None);

        var code = await svc.CreateAuthorizationCodeAsync(
            reg.ClientId, "user-1", "https://example.com/callback",
            "mcp:full", Challenge, "S256", CancellationToken.None);

        var tokens = await svc.ExchangeAuthorizationCodeAsync(
            code, reg.ClientId, "https://example.com/callback", Verifier, CancellationToken.None);

        return (svc, refreshTokens, accessTokens, reg.ClientId, tokens.RefreshToken!);
    }

    [Fact]
    public async Task ValidRefresh_RotatesTokens()
    {
        var (svc, _, _, clientId, refreshToken) = await PrepareAsync();

        var response = await svc.RefreshTokenAsync(refreshToken, clientId, CancellationToken.None);

        Assert.NotEmpty(response.AccessToken);
        Assert.NotNull(response.RefreshToken);
        Assert.NotEqual(refreshToken, response.RefreshToken);
    }

    [Fact]
    public async Task RefreshTokenReuseDetection_RevokesAllTokensForUser()
    {
        var (svc, refreshTokens, accessTokens, clientId, refreshToken) = await PrepareAsync();

        // First refresh succeeds, original token is consumed
        var secondToken = await svc.RefreshTokenAsync(refreshToken, clientId, CancellationToken.None);
        _ = secondToken; // use it

        // Attempt to reuse the original (already consumed) refresh token
        // This should trigger revoke-all and throw
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RefreshTokenAsync(refreshToken, clientId, CancellationToken.None));

        // All access tokens for the user should be revoked
        var active = await svc.GetActiveTokensForUserAsync("user-1", CancellationToken.None);
        Assert.Empty(active);
    }

    [Fact]
    public async Task ExpiredRefreshToken_ThrowsInvalidOperationException()
    {
        var (svc, refreshTokens, _, clientId, _) = await PrepareAsync();

        // Inject a fresh expired refresh token
        var plainToken = OAuthCrypto.GenerateToken();
        var tokenHash  = OAuthCrypto.HashToken(plainToken);
        await refreshTokens.AddAsync(new Geef.Atelier.Core.Domain.OAuth.OAuthRefreshToken(
            TokenHash: tokenHash,
            ClientId: clientId,
            UserId: "user-1",
            Scope: "mcp:full",
            ExpiresAt: DateTimeOffset.UtcNow.AddDays(-1),
            UsedAt: null,
            RevokedAt: null,
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-31)
        ), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RefreshTokenAsync(plainToken, clientId, CancellationToken.None));
    }

    [Fact]
    public async Task ValidRefresh_LogsTokenRefreshedEvent()
    {
        var (svc2, _, _, _, _, auditLog) = OAuthServiceFactory.Create();
        var reg = await svc2.RegisterClientAsync(
            new Geef.Atelier.Application.OAuth.ClientRegistrationRequest(
                "App", ["https://example.com/callback"], null, null),
            CancellationToken.None);
        var code = await svc2.CreateAuthorizationCodeAsync(
            reg.ClientId, "user-x", "https://example.com/callback",
            "mcp:full", Challenge, "S256", CancellationToken.None);
        var tokens = await svc2.ExchangeAuthorizationCodeAsync(
            code, reg.ClientId, "https://example.com/callback", Verifier, CancellationToken.None);

        await svc2.RefreshTokenAsync(tokens.RefreshToken!, reg.ClientId, CancellationToken.None);

        Assert.Contains(auditLog.Entries, e => e.EventType == "TokenRefreshed");
    }
}
