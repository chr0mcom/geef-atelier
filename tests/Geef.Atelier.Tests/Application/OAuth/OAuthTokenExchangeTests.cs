using Geef.Atelier.Application.OAuth;
using Geef.Atelier.Tests.Fakes.OAuth;

namespace Geef.Atelier.Tests.Application.OAuth;

public sealed class OAuthTokenExchangeTests
{
    private const string Verifier  = "MyVerifier1234567890123456789012345678901234";
    private static string Challenge => OAuthCrypto.Sha256Base64Url(Verifier);

    private static async Task<(IOAuthService svc, string clientId, string code)> PrepareFullFlowAsync()
    {
        var (svc, _, _, _, _, _) = OAuthServiceFactory.Create();
        var reg = await svc.RegisterClientAsync(
            new ClientRegistrationRequest("App", ["https://example.com/callback"], null, null),
            CancellationToken.None);

        var code = await svc.CreateAuthorizationCodeAsync(
            reg.ClientId, "user-1", "https://example.com/callback",
            "mcp:full", Challenge, "S256", CancellationToken.None);

        return (svc, reg.ClientId, code);
    }

    [Fact]
    public async Task ValidExchange_ReturnsAccessAndRefreshTokens()
    {
        var (svc, clientId, code) = await PrepareFullFlowAsync();

        var response = await svc.ExchangeAuthorizationCodeAsync(
            code, clientId, "https://example.com/callback", Verifier, CancellationToken.None);

        Assert.NotEmpty(response.AccessToken);
        Assert.NotNull(response.RefreshToken);
        Assert.NotEmpty(response.RefreshToken);
        Assert.Equal("Bearer", response.TokenType);
        Assert.True(response.ExpiresIn > 0);
    }

    [Fact]
    public async Task WrongCodeVerifier_ThrowsInvalidOperationException()
    {
        var (svc, clientId, code) = await PrepareFullFlowAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ExchangeAuthorizationCodeAsync(
                code, clientId, "https://example.com/callback", "wrong-verifier", CancellationToken.None));
    }

    [Fact]
    public async Task ExpiredCode_ThrowsInvalidOperationException()
    {
        var (svc, _, codes, _, _, _) = OAuthServiceFactory.Create(opts => opts.AuthorizationCodeTtlMinutes = 10);
        var reg = await svc.RegisterClientAsync(
            new ClientRegistrationRequest("App", ["https://example.com/callback"], null, null),
            CancellationToken.None);

        // Directly inject an already-expired code into the store
        var plainCode = OAuthCrypto.GenerateToken();
        var codeHash  = OAuthCrypto.HashToken(plainCode);
        await codes.AddAsync(new Geef.Atelier.Core.Domain.OAuth.OAuthAuthorizationCode(
            CodeHash: codeHash,
            ClientId: reg.ClientId,
            UserId: "user-1",
            RedirectUri: "https://example.com/callback",
            Scope: "mcp:full",
            CodeChallenge: Challenge,
            CodeChallengeMethod: "S256",
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(-1),  // expired
            UsedAt: null,
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-11)
        ), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ExchangeAuthorizationCodeAsync(
                plainCode, reg.ClientId, "https://example.com/callback", Verifier, CancellationToken.None));
    }

    [Fact]
    public async Task CodeReuseRejected_SecondExchangeThrows()
    {
        var (svc, clientId, code) = await PrepareFullFlowAsync();

        // First exchange succeeds
        await svc.ExchangeAuthorizationCodeAsync(
            code, clientId, "https://example.com/callback", Verifier, CancellationToken.None);

        // Second exchange must fail
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ExchangeAuthorizationCodeAsync(
                code, clientId, "https://example.com/callback", Verifier, CancellationToken.None));
    }

    [Fact]
    public async Task WrongClientId_ThrowsInvalidOperationException()
    {
        var (svc, _, code) = await PrepareFullFlowAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ExchangeAuthorizationCodeAsync(
                code, "wrong-client-id", "https://example.com/callback", Verifier, CancellationToken.None));
    }

    [Fact]
    public async Task WrongRedirectUri_ThrowsInvalidOperationException()
    {
        var (svc, clientId, code) = await PrepareFullFlowAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ExchangeAuthorizationCodeAsync(
                code, clientId, "https://attacker.com/callback", Verifier, CancellationToken.None));
    }
}
