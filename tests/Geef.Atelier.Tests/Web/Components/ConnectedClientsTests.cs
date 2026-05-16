using System.Security.Claims;
using Bunit;
using Geef.Atelier.Application.OAuth;
using Geef.Atelier.Core.Domain.OAuth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Geef.Atelier.Web.Components.Pages.Account;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class ConnectedClientsTests : TestContext
{
    private sealed class FakeAuthStateProvider(string userId) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, userId)], "Cookie");
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
        }
    }

    private sealed class StubOAuthService(
        IReadOnlyList<ConnectedClientInfo>? clients = null,
        bool revokeClientThrows = false,
        bool revokeAllThrows = false) : IOAuthService
    {
        private IReadOnlyList<ConnectedClientInfo> _clients = clients ?? [];

        public Task<IReadOnlyList<ConnectedClientInfo>> GetConnectedClientsAsync(string userId, CancellationToken ct)
            => Task.FromResult(_clients);

        public Task RevokeClientAsync(string userId, string clientId, CancellationToken ct)
        {
            if (revokeClientThrows) throw new InvalidOperationException("Revoke failed");
            _clients = _clients.Where(c => c.ClientId != clientId).ToList();
            return Task.CompletedTask;
        }

        public Task RevokeAllUserTokensAsync(string userId, CancellationToken ct)
        {
            if (revokeAllThrows) throw new InvalidOperationException("Revoke all failed");
            _clients = [];
            return Task.CompletedTask;
        }

        public Task<ClientRegistrationResult> RegisterClientAsync(ClientRegistrationRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task<AuthorizationValidationResult> ValidateAuthorizationRequestAsync(AuthorizationRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task<string> CreateAuthorizationCodeAsync(string clientId, string userId, string redirectUri, string scope, string codeChallenge, string codeChallengeMethod, CancellationToken ct) => throw new NotImplementedException();
        public Task<TokenResponse> ExchangeAuthorizationCodeAsync(string code, string clientId, string redirectUri, string codeVerifier, CancellationToken ct) => throw new NotImplementedException();
        public Task<TokenResponse> RefreshTokenAsync(string refreshToken, string clientId, CancellationToken ct) => throw new NotImplementedException();
        public Task<TokenValidationResult> ValidateAccessTokenAsync(string accessToken, CancellationToken ct) => throw new NotImplementedException();
        public Task RevokeTokenAsync(string token, string clientId, CancellationToken ct) => throw new NotImplementedException();
        public Task LogEventAsync(OAuthAuditLogEntry entry, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<OAuthAccessToken>> GetActiveTokensForUserAsync(string userId, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<OAuthClient>> GetAllClientsAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task DeleteClientAsync(string clientId, CancellationToken ct) => throw new NotImplementedException();
    }

    private static ConnectedClientInfo MakeClient(string clientId = "client-1", string name = "Test App") =>
        new(clientId, name, "mcp:full", DateTimeOffset.UtcNow.AddDays(-1));

    private void SetupServices(StubOAuthService svc)
    {
        Services.AddSingleton<IOAuthService>(svc);
        Services.AddSingleton<AuthenticationStateProvider>(new FakeAuthStateProvider("stefan"));
        Services.AddAuthorizationCore();
    }

    [Fact]
    public void ConnectedClients_NoClients_ShowsEmptyState()
    {
        SetupServices(new StubOAuthService([]));

        var cut = RenderComponent<ConnectedClients>();

        cut.Find("[data-testid='empty-state']");
        Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find("[data-testid='connected-clients-list']"));
    }

    [Fact]
    public void ConnectedClients_WithClients_ShowsClientList()
    {
        SetupServices(new StubOAuthService([MakeClient("c1", "App A"), MakeClient("c2", "App B")]));

        var cut = RenderComponent<ConnectedClients>();

        cut.Find("[data-testid='connected-clients-list']");
        Assert.Equal(2, cut.FindAll("[data-testid^='client-row-']").Count);
        Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find("[data-testid='empty-state']"));
    }

    [Fact]
    public void ConnectedClients_WithClients_ShowsRevokeButtonPerClient()
    {
        SetupServices(new StubOAuthService([MakeClient("c1")]));

        var cut = RenderComponent<ConnectedClients>();

        cut.Find("[data-testid='btn-revoke-c1']");
    }

    [Fact]
    public void ConnectedClients_ClickRevoke_ShowsConfirmationButtons()
    {
        SetupServices(new StubOAuthService([MakeClient("c1")]));

        var cut = RenderComponent<ConnectedClients>();

        cut.Find("[data-testid='btn-revoke-c1']").Click();

        cut.Find("[data-testid='btn-confirm-revoke-c1']");
        cut.Find("[data-testid='btn-cancel-revoke-c1']");
        Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find("[data-testid='btn-revoke-c1']"));
    }

    [Fact]
    public void ConnectedClients_CancelRevoke_RestoresRevokeButton()
    {
        SetupServices(new StubOAuthService([MakeClient("c1")]));

        var cut = RenderComponent<ConnectedClients>();

        cut.Find("[data-testid='btn-revoke-c1']").Click();
        cut.Find("[data-testid='btn-cancel-revoke-c1']").Click();

        cut.Find("[data-testid='btn-revoke-c1']");
        Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find("[data-testid='btn-confirm-revoke-c1']"));
    }

    [Fact]
    public async Task ConnectedClients_ConfirmRevoke_RemovesClient()
    {
        SetupServices(new StubOAuthService([MakeClient("c1"), MakeClient("c2", "App B")]));

        var cut = RenderComponent<ConnectedClients>();

        cut.Find("[data-testid='btn-revoke-c1']").Click();
        await cut.Find("[data-testid='btn-confirm-revoke-c1']").ClickAsync(new());

        Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find("[data-testid='client-row-c1']"));
        cut.Find("[data-testid='client-row-c2']");
    }

    [Fact]
    public void ConnectedClients_ClickRevokeAll_ShowsConfirmationButtons()
    {
        SetupServices(new StubOAuthService([MakeClient("c1")]));

        var cut = RenderComponent<ConnectedClients>();

        cut.Find("[data-testid='btn-revoke-all']").Click();

        cut.Find("[data-testid='btn-confirm-revoke-all']");
        cut.Find("[data-testid='btn-cancel-revoke-all']");
        Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find("[data-testid='btn-revoke-all']"));
    }

    [Fact]
    public async Task ConnectedClients_ConfirmRevokeAll_ShowsEmptyState()
    {
        SetupServices(new StubOAuthService([MakeClient("c1"), MakeClient("c2", "App B")]));

        var cut = RenderComponent<ConnectedClients>();

        cut.Find("[data-testid='btn-revoke-all']").Click();
        await cut.Find("[data-testid='btn-confirm-revoke-all']").ClickAsync(new());

        cut.Find("[data-testid='empty-state']");
    }

    [Fact]
    public void ConnectedClients_RevokeAll_Cancel_KeepsList()
    {
        SetupServices(new StubOAuthService([MakeClient("c1")]));

        var cut = RenderComponent<ConnectedClients>();

        cut.Find("[data-testid='btn-revoke-all']").Click();
        cut.Find("[data-testid='btn-cancel-revoke-all']").Click();

        cut.Find("[data-testid='btn-revoke-all']");
        cut.Find("[data-testid='connected-clients-list']");
    }

    [Fact]
    public async Task ConnectedClients_RevokeClientThrows_ShowsErrorBanner()
    {
        SetupServices(new StubOAuthService([MakeClient("c1")], revokeClientThrows: true));

        var cut = RenderComponent<ConnectedClients>();

        cut.Find("[data-testid='btn-revoke-c1']").Click();
        await cut.Find("[data-testid='btn-confirm-revoke-c1']").ClickAsync(new());

        cut.Find("[data-testid='error-banner']");
    }

    [Fact]
    public async Task ConnectedClients_RevokeAllThrows_ShowsErrorBanner()
    {
        SetupServices(new StubOAuthService([MakeClient("c1")], revokeAllThrows: true));

        var cut = RenderComponent<ConnectedClients>();

        cut.Find("[data-testid='btn-revoke-all']").Click();
        await cut.Find("[data-testid='btn-confirm-revoke-all']").ClickAsync(new());

        cut.Find("[data-testid='error-banner']");
    }
}
