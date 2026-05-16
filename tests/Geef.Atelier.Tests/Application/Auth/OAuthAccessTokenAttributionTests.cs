using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.OAuth;
using Geef.Atelier.Tests.Fakes.OAuth;

namespace Geef.Atelier.Tests.Application.Auth;

/// <summary>
/// Locks in the OAuth-MCP run-attribution chain: the username captured at the
/// consent step (passed as <c>userId</c> to <see cref="IOAuthService.CreateAuthorizationCodeAsync"/>)
/// must surface as <see cref="TokenValidationOutcome.Subject"/>. That Subject becomes
/// <c>ClaimTypes.Name</c> → <c>ICurrentUserService.Username</c> → <c>Run.CreatedByUser</c>,
/// so runs created via an OAuth-authorized MCP client are visible to the authorizing user.
/// </summary>
public sealed class OAuthAccessTokenAttributionTests
{
    private const string Verifier  = "MyVerifier1234567890123456789012345678901234";
    private static string Challenge => OAuthCrypto.Sha256Base64Url(Verifier);

    [Fact]
    public async Task ValidatedOAuthToken_SubjectIsAuthorizingUsername()
    {
        const string authorizingUsername = "alice";

        var (svc, _, _, _, _, _) = OAuthServiceFactory.Create();
        var reg = await svc.RegisterClientAsync(
            new ClientRegistrationRequest("Desktop", ["https://example.com/callback"], null, null),
            CancellationToken.None);
        var code = await svc.CreateAuthorizationCodeAsync(
            reg.ClientId, authorizingUsername, "https://example.com/callback",
            "mcp:full", Challenge, "S256", CancellationToken.None);
        var tokens = await svc.ExchangeAuthorizationCodeAsync(
            code, reg.ClientId, "https://example.com/callback", Verifier, CancellationToken.None);

        var validator = new OAuthAccessTokenValidator(svc);
        var outcome   = await validator.ValidateTokenAsync(tokens.AccessToken, CancellationToken.None);

        Assert.True(outcome.IsValid);
        Assert.Equal(authorizingUsername, outcome.Subject);
    }

    [Fact]
    public async Task RefreshedOAuthToken_KeepsAuthorizingUsernameAsSubject()
    {
        const string authorizingUsername = "bob";

        var (svc, _, _, _, _, _) = OAuthServiceFactory.Create();
        var reg = await svc.RegisterClientAsync(
            new ClientRegistrationRequest("Desktop", ["https://example.com/callback"], null, null),
            CancellationToken.None);
        var code = await svc.CreateAuthorizationCodeAsync(
            reg.ClientId, authorizingUsername, "https://example.com/callback",
            "mcp:full", Challenge, "S256", CancellationToken.None);
        var tokens = await svc.ExchangeAuthorizationCodeAsync(
            code, reg.ClientId, "https://example.com/callback", Verifier, CancellationToken.None);

        var refreshed = await svc.RefreshTokenAsync(
            tokens.RefreshToken!, reg.ClientId, CancellationToken.None);

        var validator = new OAuthAccessTokenValidator(svc);
        var outcome   = await validator.ValidateTokenAsync(refreshed.AccessToken, CancellationToken.None);

        Assert.True(outcome.IsValid);
        Assert.Equal(authorizingUsername, outcome.Subject);
    }
}
