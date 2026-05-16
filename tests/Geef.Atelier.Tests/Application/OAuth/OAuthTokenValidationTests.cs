using Geef.Atelier.Application.OAuth;
using Geef.Atelier.Tests.Fakes.OAuth;

namespace Geef.Atelier.Tests.Application.OAuth;

public sealed class OAuthTokenValidationTests
{
    private const string Verifier  = "MyVerifier1234567890123456789012345678901234";
    private static string Challenge => OAuthCrypto.Sha256Base64Url(Verifier);

    private static async Task<(IOAuthService svc, string accessToken)> IssueTokenAsync()
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
        return (svc, tokens.AccessToken);
    }

    [Fact]
    public async Task ValidAccessToken_ReturnsValid()
    {
        var (svc, accessToken) = await IssueTokenAsync();

        var result = await svc.ValidateAccessTokenAsync(accessToken, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal("user-1", result.UserId);
        Assert.Equal("mcp:full", result.Scope);
    }

    [Fact]
    public async Task ExpiredAccessToken_ReturnsInvalid()
    {
        var (svc, _, _, accessTokens, _, _) = OAuthServiceFactory.Create();

        // Inject an expired access token directly
        var plainToken = OAuthCrypto.GenerateToken();
        var tokenHash  = OAuthCrypto.HashToken(plainToken);
        await accessTokens.AddAsync(new Geef.Atelier.Core.Domain.OAuth.OAuthAccessToken(
            TokenHash: tokenHash,
            ClientId: "client-1",
            UserId: "user-1",
            Scope: "mcp:full",
            ExpiresAt: DateTimeOffset.UtcNow.AddHours(-1),
            RevokedAt: null,
            CreatedAt: DateTimeOffset.UtcNow.AddHours(-2)
        ), CancellationToken.None);

        var result = await svc.ValidateAccessTokenAsync(plainToken, CancellationToken.None);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task RevokedAccessToken_ReturnsInvalid()
    {
        var (svc, _, _, accessTokens, _, _) = OAuthServiceFactory.Create();

        var plainToken = OAuthCrypto.GenerateToken();
        var tokenHash  = OAuthCrypto.HashToken(plainToken);
        await accessTokens.AddAsync(new Geef.Atelier.Core.Domain.OAuth.OAuthAccessToken(
            TokenHash: tokenHash,
            ClientId: "client-1",
            UserId: "user-1",
            Scope: "mcp:full",
            ExpiresAt: DateTimeOffset.UtcNow.AddHours(1),
            RevokedAt: DateTimeOffset.UtcNow.AddMinutes(-5),   // already revoked
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-10)
        ), CancellationToken.None);

        var result = await svc.ValidateAccessTokenAsync(plainToken, CancellationToken.None);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task UnknownToken_ReturnsInvalid()
    {
        var (svc, _, _, _, _, _) = OAuthServiceFactory.Create();

        var result = await svc.ValidateAccessTokenAsync("nonexistent-token", CancellationToken.None);

        Assert.False(result.IsValid);
    }
}
