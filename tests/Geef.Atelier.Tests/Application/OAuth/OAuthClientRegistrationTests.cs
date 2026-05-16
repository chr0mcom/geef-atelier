using Geef.Atelier.Application.OAuth;
using Geef.Atelier.Tests.Fakes.OAuth;

namespace Geef.Atelier.Tests.Application.OAuth;

public sealed class OAuthClientRegistrationTests
{
    [Fact]
    public async Task ValidRegistration_ReturnsNonEmptyClientId()
    {
        var (svc, clients, _, _, _, _) = OAuthServiceFactory.Create();
        var request = new ClientRegistrationRequest(
            ClientName: "TestApp",
            RedirectUris: ["https://example.com/callback"],
            LogoUri: null,
            ClientUri: null);

        var result = await svc.RegisterClientAsync(request, CancellationToken.None);

        Assert.NotEmpty(result.ClientId);
        Assert.NotNull(await clients.FindByClientIdAsync(result.ClientId, CancellationToken.None));
    }

    [Fact]
    public async Task ValidRegistration_ClientIdIssuedAtIsRecent()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var (svc, _, _, _, _, _) = OAuthServiceFactory.Create();
        var request = new ClientRegistrationRequest("App", ["https://example.com/cb"], null, null);

        var result = await svc.RegisterClientAsync(request, CancellationToken.None);

        Assert.True(result.ClientIdIssuedAt >= before);
        Assert.True(result.ClientIdIssuedAt <= DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task ValidRegistration_LogsClientRegisteredEvent()
    {
        var (svc, _, _, _, _, auditLog) = OAuthServiceFactory.Create();
        var request = new ClientRegistrationRequest("App", ["https://example.com/cb"], null, null);

        var result = await svc.RegisterClientAsync(request, CancellationToken.None);

        Assert.Single(auditLog.Entries, e => e.EventType == "ClientRegistered" && e.ClientId == result.ClientId);
    }

    [Fact]
    public async Task MultipleRegistrations_EachGetDistinctClientId()
    {
        var (svc, _, _, _, _, _) = OAuthServiceFactory.Create();
        var request = new ClientRegistrationRequest("App", ["https://example.com/cb"], null, null);

        var r1 = await svc.RegisterClientAsync(request, CancellationToken.None);
        var r2 = await svc.RegisterClientAsync(request, CancellationToken.None);

        Assert.NotEqual(r1.ClientId, r2.ClientId);
    }
}
