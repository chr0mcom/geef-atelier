using Geef.Atelier.Application.OAuth;
using Geef.Atelier.Tests.Fakes.OAuth;

namespace Geef.Atelier.Tests.Application.OAuth;

public sealed class OAuthRevokeTests
{
    private const string Verifier  = "MyVerifier1234567890123456789012345678901234";
    private static string Challenge => OAuthCrypto.Sha256Base64Url(Verifier);

    private static async Task<(IOAuthService svc, string clientId, string accessToken, string refreshToken)> IssueTokensAsync()
    {
        var (svc, _, _, _, _, _) = OAuthServiceFactory.Create();
        var reg = await svc.RegisterClientAsync(
            new Geef.Atelier.Application.OAuth.ClientRegistrationRequest(
                "App", ["https://example.com/callback"], null, null),
            CancellationToken.None);
        var code = await svc.CreateAuthorizationCodeAsync(
            reg.ClientId, "user-1", "https://example.com/callback",
            "mcp:full", Challenge, "S256", CancellationToken.None);
        var tokens = await svc.ExchangeAuthorizationCodeAsync(
            code, reg.ClientId, "https://example.com/callback", Verifier, CancellationToken.None);
        return (svc, reg.ClientId, tokens.AccessToken, tokens.RefreshToken!);
    }

    [Fact]
    public async Task RevokeAccessToken_MarksAsRevoked()
    {
        var (svc, clientId, accessToken, _) = await IssueTokensAsync();

        // Validate the token is initially valid
        var before = await svc.ValidateAccessTokenAsync(accessToken, CancellationToken.None);
        Assert.True(before.IsValid);

        await svc.RevokeTokenAsync(accessToken, clientId, CancellationToken.None);

        var after = await svc.ValidateAccessTokenAsync(accessToken, CancellationToken.None);
        Assert.False(after.IsValid);
    }

    [Fact]
    public async Task RevokeAllUserTokens_RevokesEverything()
    {
        var (svc, clientId, accessToken1, _) = await IssueTokensAsync();

        // Issue a second token for the same user
        var reg2 = await svc.RegisterClientAsync(
            new Geef.Atelier.Application.OAuth.ClientRegistrationRequest(
                "App2", ["https://other.com/callback"], null, null),
            CancellationToken.None);
        var code2 = await svc.CreateAuthorizationCodeAsync(
            reg2.ClientId, "user-1", "https://other.com/callback",
            "mcp:full", Challenge, "S256", CancellationToken.None);
        var tokens2 = await svc.ExchangeAuthorizationCodeAsync(
            code2, reg2.ClientId, "https://other.com/callback", Verifier, CancellationToken.None);

        await svc.RevokeAllUserTokensAsync("user-1", CancellationToken.None);

        var active = await svc.GetActiveTokensForUserAsync("user-1", CancellationToken.None);
        Assert.Empty(active);

        var v1 = await svc.ValidateAccessTokenAsync(accessToken1, CancellationToken.None);
        var v2 = await svc.ValidateAccessTokenAsync(tokens2.AccessToken, CancellationToken.None);
        Assert.False(v1.IsValid);
        Assert.False(v2.IsValid);
    }

    [Fact]
    public async Task RevokeToken_LogsTokenRevokedEvent()
    {
        var (svc, _, _, _, _, auditLog) = OAuthServiceFactory.Create();
        var reg = await svc.RegisterClientAsync(
            new Geef.Atelier.Application.OAuth.ClientRegistrationRequest(
                "App", ["https://example.com/callback"], null, null),
            CancellationToken.None);
        var code = await svc.CreateAuthorizationCodeAsync(
            reg.ClientId, "user-1", "https://example.com/callback",
            "mcp:full", Challenge, "S256", CancellationToken.None);
        var tokens = await svc.ExchangeAuthorizationCodeAsync(
            code, reg.ClientId, "https://example.com/callback", Verifier, CancellationToken.None);

        await svc.RevokeTokenAsync(tokens.AccessToken, reg.ClientId, CancellationToken.None);

        Assert.Contains(auditLog.Entries, e => e.EventType == "TokenRevoked");
    }

    [Fact]
    public async Task RevokeAccessToken_WrongClientId_DoesNotRevokeToken()
    {
        var (svc, clientId, accessToken, _) = await IssueTokensAsync();

        // A different client attempts to revoke the token — must silently no-op (RFC 7009 §2.1)
        await svc.RevokeTokenAsync(accessToken, "wrong-client-id", CancellationToken.None);

        var result = await svc.ValidateAccessTokenAsync(accessToken, CancellationToken.None);
        Assert.True(result.IsValid, "Token must remain valid when revoked by a non-owning client");
    }

    [Fact]
    public async Task RevokeAccessToken_WrongClientId_ProducesNoAuditEvent()
    {
        var (svc, _, _, _, _, auditLog) = OAuthServiceFactory.Create();
        var reg = await svc.RegisterClientAsync(
            new Geef.Atelier.Application.OAuth.ClientRegistrationRequest(
                "App", ["https://example.com/callback"], null, null),
            CancellationToken.None);
        var code = await svc.CreateAuthorizationCodeAsync(
            reg.ClientId, "user-1", "https://example.com/callback",
            "mcp:full", Challenge, "S256", CancellationToken.None);
        var tokens = await svc.ExchangeAuthorizationCodeAsync(
            code, reg.ClientId, "https://example.com/callback", Verifier, CancellationToken.None);

        var countBefore = auditLog.Entries.Count;
        await svc.RevokeTokenAsync(tokens.AccessToken, "wrong-client-id", CancellationToken.None);

        Assert.Equal(countBefore, auditLog.Entries.Count);
    }

    [Fact]
    public async Task RevokeRefreshToken_WrongClientId_DoesNotRevokeToken()
    {
        var (svc, clientId, _, refreshToken) = await IssueTokensAsync();

        // A different client attempts to revoke the refresh token — must silently no-op (RFC 7009 §2.1)
        await svc.RevokeTokenAsync(refreshToken, "wrong-client-id", CancellationToken.None);

        // Re-issuing a token via the refresh token proves it was not revoked
        var result = await svc.RefreshTokenAsync(refreshToken, clientId, CancellationToken.None);
        Assert.NotNull(result.AccessToken);
    }
}
