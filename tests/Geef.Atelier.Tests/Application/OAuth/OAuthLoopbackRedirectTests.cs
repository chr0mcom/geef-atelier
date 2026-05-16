using Geef.Atelier.Application.OAuth;
using Geef.Atelier.Tests.Fakes.OAuth;

namespace Geef.Atelier.Tests.Application.OAuth;

public sealed class OAuthLoopbackRedirectTests
{
    private static string Challenge => OAuthCrypto.Sha256Base64Url("SomeVerifier12345678901234567890123456789012345");

    [Fact]
    public async Task LoopbackUri_DifferentPort_IsAccepted()
    {
        // Registered URI uses a fixed port; requested URI uses an ephemeral port
        var (svc, _, _, _, _, _) = OAuthServiceFactory.Create();
        await svc.RegisterClientAsync(
            new Geef.Atelier.Application.OAuth.ClientRegistrationRequest(
                "App", ["http://127.0.0.1/callback"], null, null),
            CancellationToken.None);

        // Must find the client first to get its actual ID
        var (svc2, clients, _, _, _, _) = OAuthServiceFactory.Create();
        var reg = await svc2.RegisterClientAsync(
            new Geef.Atelier.Application.OAuth.ClientRegistrationRequest(
                "App", ["http://127.0.0.1/callback"], null, null),
            CancellationToken.None);

        var request = new Geef.Atelier.Application.OAuth.AuthorizationRequest(
            ResponseType: "code",
            ClientId: reg.ClientId,
            RedirectUri: "http://127.0.0.1:54321/callback",  // different port
            Scope: "mcp:full",
            State: null,
            CodeChallenge: Challenge,
            CodeChallengeMethod: "S256");

        var result = await svc2.ValidateAuthorizationRequestAsync(request, CancellationToken.None);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task NonLoopbackUri_DifferentPort_IsRejected()
    {
        var (svc, _, _, _, _, _) = OAuthServiceFactory.Create();
        var reg = await svc.RegisterClientAsync(
            new Geef.Atelier.Application.OAuth.ClientRegistrationRequest(
                "App", ["https://example.com/callback"], null, null),
            CancellationToken.None);

        var request = new Geef.Atelier.Application.OAuth.AuthorizationRequest(
            ResponseType: "code",
            ClientId: reg.ClientId,
            RedirectUri: "https://example.com:8080/callback",  // different port, not loopback
            Scope: "mcp:full",
            State: null,
            CodeChallenge: Challenge,
            CodeChallengeMethod: "S256");

        var result = await svc.ValidateAuthorizationRequestAsync(request, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("invalid_request", result.ErrorCode);
    }

    [Fact]
    public async Task ExactMatch_IsAlwaysAccepted()
    {
        var (svc, _, _, _, _, _) = OAuthServiceFactory.Create();
        var reg = await svc.RegisterClientAsync(
            new Geef.Atelier.Application.OAuth.ClientRegistrationRequest(
                "App", ["https://example.com/callback"], null, null),
            CancellationToken.None);

        var request = new Geef.Atelier.Application.OAuth.AuthorizationRequest(
            ResponseType: "code",
            ClientId: reg.ClientId,
            RedirectUri: "https://example.com/callback",  // exact match
            Scope: "mcp:full",
            State: null,
            CodeChallenge: Challenge,
            CodeChallengeMethod: "S256");

        var result = await svc.ValidateAuthorizationRequestAsync(request, CancellationToken.None);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task LocalhostLoopback_DifferentPort_IsRejected()
    {
        // RFC 8252 §7.3: only 127.0.0.1 receives loopback port-flexibility, not "localhost"
        // (localhost may resolve to IPv6 ::1 depending on OS configuration).
        var (svc, _, _, _, _, _) = OAuthServiceFactory.Create();
        var reg = await svc.RegisterClientAsync(
            new Geef.Atelier.Application.OAuth.ClientRegistrationRequest(
                "App", ["http://localhost/callback"], null, null),
            CancellationToken.None);

        var request = new Geef.Atelier.Application.OAuth.AuthorizationRequest(
            ResponseType: "code",
            ClientId: reg.ClientId,
            RedirectUri: "http://localhost:8080/callback",  // different port, but localhost ≠ 127.0.0.1
            Scope: "mcp:full",
            State: null,
            CodeChallenge: Challenge,
            CodeChallengeMethod: "S256");

        var result = await svc.ValidateAuthorizationRequestAsync(request, CancellationToken.None);

        Assert.False(result.IsValid);  // no loopback treatment for "localhost"
        Assert.Equal("invalid_request", result.ErrorCode);
    }
}
