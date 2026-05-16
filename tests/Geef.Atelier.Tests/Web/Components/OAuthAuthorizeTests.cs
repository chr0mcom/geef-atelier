using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.OAuth;
using Geef.Atelier.Core.Domain.OAuth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Geef.Atelier.Web.Components.Pages;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class OAuthAuthorizeTests : TestContext
{
    private sealed class StubOAuthService(AuthorizationValidationResult validateResult) : IOAuthService
    {
        public Task<AuthorizationValidationResult> ValidateAuthorizationRequestAsync(AuthorizationRequest request, CancellationToken ct)
            => Task.FromResult(validateResult);

        public Task<ClientRegistrationResult> RegisterClientAsync(ClientRegistrationRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task<string> CreateAuthorizationCodeAsync(string clientId, string userId, string redirectUri, string scope, string codeChallenge, string codeChallengeMethod, CancellationToken ct) => throw new NotImplementedException();
        public Task<TokenResponse> ExchangeAuthorizationCodeAsync(string code, string clientId, string redirectUri, string codeVerifier, CancellationToken ct) => throw new NotImplementedException();
        public Task<TokenResponse> RefreshTokenAsync(string refreshToken, string clientId, CancellationToken ct) => throw new NotImplementedException();
        public Task<TokenValidationResult> ValidateAccessTokenAsync(string accessToken, CancellationToken ct) => throw new NotImplementedException();
        public Task RevokeTokenAsync(string token, string clientId, CancellationToken ct) => throw new NotImplementedException();
        public Task RevokeAllUserTokensAsync(string userId, CancellationToken ct) => throw new NotImplementedException();
        public Task RevokeClientAsync(string userId, string clientId, CancellationToken ct) => throw new NotImplementedException();
        public Task LogEventAsync(OAuthAuditLogEntry entry, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<OAuthAccessToken>> GetActiveTokensForUserAsync(string userId, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<ConnectedClientInfo>> GetConnectedClientsAsync(string userId, CancellationToken ct) => throw new NotImplementedException();
    }

    private static HttpContext GetContext(string method = "GET", string userName = "stefan")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = method;
        ctx.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.Name, userName)], "Cookie"));
        return ctx;
    }

    private static OAuthClient FakeClient(string? clientUri = null) => new(
        ClientId: "test-client",
        ClientName: "Test App",
        RedirectUris: ["https://example.com/callback"],
        ClientSecretHash: null,
        LogoUri: null,
        ClientUri: clientUri,
        IsPublic: true,
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow);

    private const string ValidQueryString =
        "/oauth/authorize?response_type=code&client_id=test-client&redirect_uri=https://example.com/callback&scope=mcp:full&state=s&code_challenge=abc123&code_challenge_method=S256";

    private void NavigateTo(string uri) =>
        Services.GetRequiredService<FakeNavigationManager>().NavigateTo(uri);

    [Fact]
    public void OAuthAuthorize_MissingRequiredParams_ShowsErrorMessage()
    {
        Services.AddSingleton<IOAuthService>(new StubOAuthService(
            new AuthorizationValidationResult(true, null, null, FakeClient())));

        NavigateTo("/oauth/authorize");

        var cut = RenderComponent<OAuthAuthorize>(p => p
            .AddCascadingValue<HttpContext>(GetContext()));

        cut.Find("[data-testid='oauth-error']");
        Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find("[data-testid='btn-approve']"));
    }

    [Fact]
    public void OAuthAuthorize_ServiceReturnsInvalid_ShowsErrorMessage()
    {
        Services.AddSingleton<IOAuthService>(new StubOAuthService(
            new AuthorizationValidationResult(false, "invalid_client", "Unknown client_id", null)));

        NavigateTo(ValidQueryString);

        var cut = RenderComponent<OAuthAuthorize>(p => p
            .AddCascadingValue<HttpContext>(GetContext()));

        cut.Find("[data-testid='oauth-error']");
        Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find("[data-testid='btn-approve']"));
    }

    [Fact]
    public void OAuthAuthorize_ValidRequest_ShowsApproveAndDenyButtons()
    {
        Services.AddSingleton<IOAuthService>(new StubOAuthService(
            new AuthorizationValidationResult(true, null, null, FakeClient())));

        NavigateTo(ValidQueryString);

        var cut = RenderComponent<OAuthAuthorize>(p => p
            .AddCascadingValue<HttpContext>(GetContext()));

        cut.Find("[data-testid='btn-approve']");
        cut.Find("[data-testid='btn-deny']");
        Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find("[data-testid='oauth-error']"));
    }

    [Fact]
    public void OAuthAuthorize_ValidRequest_ShowsClientName()
    {
        Services.AddSingleton<IOAuthService>(new StubOAuthService(
            new AuthorizationValidationResult(true, null, null, FakeClient())));

        NavigateTo(ValidQueryString);

        var cut = RenderComponent<OAuthAuthorize>(p => p
            .AddCascadingValue<HttpContext>(GetContext()));

        Assert.Contains("Test App", cut.Markup);
    }

    [Fact]
    public void OAuthAuthorize_ClientWithClientUri_ShowsClientUriLink()
    {
        Services.AddSingleton<IOAuthService>(new StubOAuthService(
            new AuthorizationValidationResult(true, null, null, FakeClient(clientUri: "https://testapp.example.com"))));

        NavigateTo(ValidQueryString);

        var cut = RenderComponent<OAuthAuthorize>(p => p
            .AddCascadingValue<HttpContext>(GetContext()));

        var link = cut.Find("[data-testid='client-uri']");
        Assert.Equal("https://testapp.example.com", link.GetAttribute("href"));
    }

    [Fact]
    public void OAuthAuthorize_ClientWithoutClientUri_NoClientUriLink()
    {
        Services.AddSingleton<IOAuthService>(new StubOAuthService(
            new AuthorizationValidationResult(true, null, null, FakeClient(clientUri: null))));

        NavigateTo(ValidQueryString);

        var cut = RenderComponent<OAuthAuthorize>(p => p
            .AddCascadingValue<HttpContext>(GetContext()));

        Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find("[data-testid='client-uri']"));
    }
}
