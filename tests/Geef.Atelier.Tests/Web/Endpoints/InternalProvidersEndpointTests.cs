using System.Net;
using System.Text.Json;
using Geef.Atelier.Tests.EndToEnd;
using Geef.Atelier.Tests.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Endpoints;

/// <summary>
/// <c>GET /api/internal/providers/cli</c> answers only a caller who sends the configured internal token; without a configured token
/// it answers nobody.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class InternalProvidersEndpointTests(PostgresFixture postgres)
{
    private const string Address = "/api/internal/providers/cli";
    private const string Configured = "internal-test-token-0123456789";

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WithoutAConfiguredTokenNobodyGetsAnAnswer(string configured)
    {
        await using var host = await StartAsync(configured);
        using var client = host.CreateAnonymousClient();

        foreach (var sent in new[] { null, "", configured, "anything" })
            await AssertRefusedAsync(client, sent);
    }

    [Fact]
    public async Task ASignInDoesNotReplaceTheToken()
    {
        await using var host = await StartAsync("");
        using var admin = await host.CreateSignedInClientAsync("internal-admin", isAdmin: true);

        await AssertRefusedAsync(admin, null);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("internal-test-token-012345678")]
    [InlineData("internal-test-token-0123456789x")]
    [InlineData("INTERNAL-TEST-TOKEN-0123456789")]
    public async Task AMissingOrWrongTokenIsRefused(string? sent)
    {
        await using var host = await StartAsync(Configured);
        using var client = host.CreateAnonymousClient();

        await AssertRefusedAsync(client, sent);
    }

    [Fact]
    public async Task TheConfiguredTokenGetsTheBuiltInCliProviders()
    {
        await using var host = await StartAsync(Configured);
        using var client = host.CreateAnonymousClient();

        using var response = await GetAsync(client, Configured);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var names = body.RootElement.EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToList();
        Assert.Contains("claude-cli", names);
        Assert.Contains("codex-cli", names);
    }

    private async Task<AtelierEndToEndHost> StartAsync(string configured)
    {
        var host = await AtelierEndToEndHost.StartAsync(postgres);
        host.Services.GetRequiredService<IConfiguration>()["CliProxy:InternalApiToken"] = configured;
        return host;
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Address);
        if (token is not null)
            request.Headers.TryAddWithoutValidation("X-Internal-Token", token);
        return await client.SendAsync(request);
    }

    private static async Task AssertRefusedAsync(HttpClient client, string? token)
    {
        using var response = await GetAsync(client, token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }
}
