using Geef.Atelier.Application.OAuth;
using Geef.Atelier.Tests.Fakes.OAuth;

namespace Geef.Atelier.Tests.Application.OAuth;

public sealed class OAuthAuthorizationCodeTests
{
    private static readonly string ValidChallenge = OAuthCrypto.Sha256Base64Url("MyVerifier123456789012345678901234567890");

    private static async Task<(IOAuthService svc, string clientId)> CreateServiceWithClientAsync()
    {
        var (svc, _, _, _, _, _) = OAuthServiceFactory.Create();
        var reg = await svc.RegisterClientAsync(
            new ClientRegistrationRequest("App", ["https://example.com/callback"], null, null),
            CancellationToken.None);
        return (svc, reg.ClientId);
    }

    [Fact]
    public async Task ValidAuthorizationRequest_ReturnsValid()
    {
        var (svc, clientId) = await CreateServiceWithClientAsync();
        var request = new AuthorizationRequest(
            ResponseType: "code",
            ClientId: clientId,
            RedirectUri: "https://example.com/callback",
            Scope: "mcp:full",
            State: null,
            CodeChallenge: ValidChallenge,
            CodeChallengeMethod: "S256");

        var result = await svc.ValidateAuthorizationRequestAsync(request, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task MissingCodeChallenge_ReturnsInvalid()
    {
        var (svc, clientId) = await CreateServiceWithClientAsync();
        var request = new AuthorizationRequest(
            ResponseType: "code",
            ClientId: clientId,
            RedirectUri: "https://example.com/callback",
            Scope: "mcp:full",
            State: null,
            CodeChallenge: null,
            CodeChallengeMethod: "S256");

        var result = await svc.ValidateAuthorizationRequestAsync(request, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("invalid_request", result.ErrorCode);
    }

    [Fact]
    public async Task WrongCodeChallengeMethod_PlainIsRejected()
    {
        var (svc, clientId) = await CreateServiceWithClientAsync();
        var request = new AuthorizationRequest(
            ResponseType: "code",
            ClientId: clientId,
            RedirectUri: "https://example.com/callback",
            Scope: "mcp:full",
            State: null,
            CodeChallenge: ValidChallenge,
            CodeChallengeMethod: "plain");

        var result = await svc.ValidateAuthorizationRequestAsync(request, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("invalid_request", result.ErrorCode);
    }

    [Fact]
    public async Task UnknownClientId_ReturnsInvalid()
    {
        var (svc, _, _, _, _, _) = OAuthServiceFactory.Create();
        var request = new AuthorizationRequest(
            ResponseType: "code",
            ClientId: "nonexistent-client",
            RedirectUri: "https://example.com/callback",
            Scope: "mcp:full",
            State: null,
            CodeChallenge: ValidChallenge,
            CodeChallengeMethod: "S256");

        var result = await svc.ValidateAuthorizationRequestAsync(request, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("invalid_client", result.ErrorCode);
    }

    [Fact]
    public async Task WrongResponseType_ReturnsInvalid()
    {
        var (svc, clientId) = await CreateServiceWithClientAsync();
        var request = new AuthorizationRequest(
            ResponseType: "token",
            ClientId: clientId,
            RedirectUri: "https://example.com/callback",
            Scope: "mcp:full",
            State: null,
            CodeChallenge: ValidChallenge,
            CodeChallengeMethod: "S256");

        var result = await svc.ValidateAuthorizationRequestAsync(request, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("unsupported_response_type", result.ErrorCode);
    }

    [Fact]
    public async Task MismatchedRedirectUri_ReturnsInvalid()
    {
        var (svc, clientId) = await CreateServiceWithClientAsync();
        var request = new AuthorizationRequest(
            ResponseType: "code",
            ClientId: clientId,
            RedirectUri: "https://attacker.com/callback",
            Scope: "mcp:full",
            State: null,
            CodeChallenge: ValidChallenge,
            CodeChallengeMethod: "S256");

        var result = await svc.ValidateAuthorizationRequestAsync(request, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("invalid_request", result.ErrorCode);
    }
}
