using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Text.Json;
using Testcontainers.PostgreSql;

namespace Geef.Atelier.Tests.Web.Endpoints;

[Trait("Category", "Integration")]
public sealed class OAuthEndpointTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private WebApplicationFactory<Program> CreateFactory()
        => new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<AtelierDbContext>));
                if (descriptor is not null) services.Remove(descriptor);

                services.AddDbContext<AtelierDbContext>(options =>
                    options.UseNpgsql(_postgres.GetConnectionString())
                           .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
            }));

    private async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider
            .GetRequiredService<AtelierDbContext>()
            .Database.MigrateAsync();
    }

    [Fact]
    public async Task WellKnown_AuthorizationServer_ReturnsRfc8414Shape()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/.well-known/oauth-authorization-server");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("issuer", out _), "Missing 'issuer'");
        Assert.True(json.TryGetProperty("authorization_endpoint", out _), "Missing 'authorization_endpoint'");
        Assert.True(json.TryGetProperty("token_endpoint", out _), "Missing 'token_endpoint'");
        Assert.True(json.TryGetProperty("code_challenge_methods_supported", out var methods),
            "Missing 'code_challenge_methods_supported'");
        var arr = methods.EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("S256", arr);
    }

    [Fact]
    public async Task WellKnown_ProtectedResource_ReturnsMcpShape()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/.well-known/oauth-protected-resource");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("resource", out _), "Missing 'resource'");
        Assert.True(json.TryGetProperty("authorization_servers", out _), "Missing 'authorization_servers'");
    }

    [Fact]
    public async Task Register_ValidRequest_Returns201WithClientId()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var body = new
        {
            client_name    = "TestClient",
            redirect_uris  = new[] { "https://example.com/callback" }
        };
        var response = await client.PostAsJsonAsync("/oauth/register", body);

        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("client_id", out var clientId), "Missing 'client_id'");
        Assert.NotEmpty(clientId.GetString()!);
    }

    [Fact]
    public async Task Register_MissingRedirectUris_Returns400()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var body = new { client_name = "TestClient" };
        var response = await client.PostAsJsonAsync("/oauth/register", body);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_MissingClientName_Returns400()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var body = new { redirect_uris = new[] { "https://example.com/cb" } };
        var response = await client.PostAsJsonAsync("/oauth/register", body);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Token_NoGrantType_Returns400()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var form     = new FormUrlEncodedContent([]);
        var response = await client.PostAsync("/oauth/token", form);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Authorize_WithoutCookie_RedirectsToLogin()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/oauth/authorize?response_type=code&client_id=x&redirect_uri=http://localhost/cb&code_challenge=abc&code_challenge_method=S256");

        // Without a cookie the page should require auth — either redirect or 401
        Assert.True(
            response.StatusCode == System.Net.HttpStatusCode.Redirect ||
            response.StatusCode == System.Net.HttpStatusCode.Found ||
            response.StatusCode == System.Net.HttpStatusCode.Unauthorized,
            $"Expected redirect or 401 but got {response.StatusCode}");
    }

    [Fact]
    public async Task WellKnown_AuthorizationServer_ContainsBearerDiscovery()
    {
        // The WWW-Authenticate header is tested separately via TestServer.
        // This test verifies the .well-known endpoint returns the discovery URLs.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/.well-known/oauth-protected-resource");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("authorization_servers", out _), "Missing 'authorization_servers'");
    }
}
