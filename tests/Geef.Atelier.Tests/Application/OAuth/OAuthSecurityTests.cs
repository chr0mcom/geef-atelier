using Geef.Atelier.Application.OAuth;
using Geef.Atelier.Tests.Fakes.OAuth;

namespace Geef.Atelier.Tests.Application.OAuth;

/// <summary>MANDATORY security tests — each covers a distinct attack vector.</summary>
public sealed class OAuthSecurityTests
{
    private const string Verifier  = "MyVerifier1234567890123456789012345678901234";
    private static string Challenge => OAuthCrypto.Sha256Base64Url(Verifier);

    [Fact]
    public async Task NoPkce_RegistrationRequest_AuthorizationIsRejected()
    {
        var (svc, _, _, _, _, _) = OAuthServiceFactory.Create();
        var reg = await svc.RegisterClientAsync(
            new ClientRegistrationRequest("App", ["https://example.com/callback"], null, null),
            CancellationToken.None);

        var request = new AuthorizationRequest(
            ResponseType: "code",
            ClientId: reg.ClientId,
            RedirectUri: "https://example.com/callback",
            Scope: "mcp:full",
            State: null,
            CodeChallenge: null,      // no PKCE
            CodeChallengeMethod: null);

        var result = await svc.ValidateAuthorizationRequestAsync(request, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("invalid_request", result.ErrorCode);
    }

    [Fact]
    public async Task PlainPkceMethod_IsRejected()
    {
        var (svc, _, _, _, _, _) = OAuthServiceFactory.Create();
        var reg = await svc.RegisterClientAsync(
            new ClientRegistrationRequest("App", ["https://example.com/callback"], null, null),
            CancellationToken.None);

        var request = new AuthorizationRequest(
            ResponseType: "code",
            ClientId: reg.ClientId,
            RedirectUri: "https://example.com/callback",
            Scope: "mcp:full",
            State: null,
            CodeChallenge: Verifier,  // plain: challenge == verifier
            CodeChallengeMethod: "plain");

        var result = await svc.ValidateAuthorizationRequestAsync(request, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("invalid_request", result.ErrorCode);
    }

    [Fact]
    public async Task CodeReuse_SecondExchange_ThrowsException()
    {
        var (svc, _, _, _, _, _) = OAuthServiceFactory.Create();
        var reg = await svc.RegisterClientAsync(
            new ClientRegistrationRequest("App", ["https://example.com/callback"], null, null),
            CancellationToken.None);
        var code = await svc.CreateAuthorizationCodeAsync(
            reg.ClientId, "user-1", "https://example.com/callback",
            "mcp:full", Challenge, "S256", CancellationToken.None);

        await svc.ExchangeAuthorizationCodeAsync(
            code, reg.ClientId, "https://example.com/callback", Verifier, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ExchangeAuthorizationCodeAsync(
                code, reg.ClientId, "https://example.com/callback", Verifier, CancellationToken.None));
    }

    [Fact]
    public async Task WrongClient_TokenExchange_ThrowsException()
    {
        var (svc, _, _, _, _, _) = OAuthServiceFactory.Create();
        await svc.RegisterClientAsync(
            new ClientRegistrationRequest("App", ["https://example.com/callback"], null, null),
            CancellationToken.None);
        var reg = await svc.RegisterClientAsync(
            new ClientRegistrationRequest("App", ["https://example.com/callback"], null, null),
            CancellationToken.None);
        var code = await svc.CreateAuthorizationCodeAsync(
            reg.ClientId, "user-1", "https://example.com/callback",
            "mcp:full", Challenge, "S256", CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ExchangeAuthorizationCodeAsync(
                code, "some-other-client", "https://example.com/callback", Verifier, CancellationToken.None));
    }

    [Fact]
    public async Task WrongRedirectUri_TokenExchange_ThrowsException()
    {
        var (svc, _, _, _, _, _) = OAuthServiceFactory.Create();
        var reg = await svc.RegisterClientAsync(
            new ClientRegistrationRequest("App", ["https://example.com/callback"], null, null),
            CancellationToken.None);
        var code = await svc.CreateAuthorizationCodeAsync(
            reg.ClientId, "user-1", "https://example.com/callback",
            "mcp:full", Challenge, "S256", CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ExchangeAuthorizationCodeAsync(
                code, reg.ClientId, "https://attacker.com/steal", Verifier, CancellationToken.None));
    }

    [Fact]
    public async Task RefreshTokenReuse_RevokesAllTokensForUser()
    {
        var (svc, _, _, _, _, _) = OAuthServiceFactory.Create();
        var reg = await svc.RegisterClientAsync(
            new ClientRegistrationRequest("App", ["https://example.com/callback"], null, null),
            CancellationToken.None);
        var code = await svc.CreateAuthorizationCodeAsync(
            reg.ClientId, "user-1", "https://example.com/callback",
            "mcp:full", Challenge, "S256", CancellationToken.None);
        var initial = await svc.ExchangeAuthorizationCodeAsync(
            code, reg.ClientId, "https://example.com/callback", Verifier, CancellationToken.None);

        // First refresh — legitimate use
        await svc.RefreshTokenAsync(initial.RefreshToken!, reg.ClientId, CancellationToken.None);

        // Attacker replays the original refresh token (reuse attack)
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RefreshTokenAsync(initial.RefreshToken!, reg.ClientId, CancellationToken.None));

        // All tokens for the user must have been revoked
        var active = await svc.GetActiveTokensForUserAsync("user-1", CancellationToken.None);
        Assert.Empty(active);
    }
}
