using Geef.Atelier.Application.Auth;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Tests.Fakes.OAuth;
using Geef.Atelier.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Web.Auth;

/// <summary>Ensures Claude Code CLI with static bearer token still works after OAuth introduction.</summary>
public sealed class BackwardsCompatTests
{
    private static Task<IHost> BuildHostAsync(ITokenValidator validator)
        => new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddSingleton(validator);
                    services.AddAuthentication()
                        .AddScheme<AuthenticationSchemeOptions, BearerTokenHandler>("Bearer", _ => { });
                    services.AddAuthorization(o =>
                        o.AddPolicy("McpPolicy", p =>
                        {
                            p.AuthenticationSchemes = ["Bearer"];
                            p.RequireAuthenticatedUser();
                        }));
                    services.AddRouting();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(ep => ep.MapGet("/mcp", () => "ok").RequireAuthorization("McpPolicy"));
                });
            })
            .StartAsync();

    [Fact]
    public async Task McpStaticBearerTokenStillWorks()
    {
        const string knownToken = "super-secret-static-token";
        var validator = new FixedTokenValidator(knownToken);

        var host   = await BuildHostAsync(validator);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {knownToken}");

        var response = await client.GetAsync("/mcp");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        await host.StopAsync();
    }

    [Fact]
    public async Task ClaudeCodeCliCompatibility_StaticBearerOutcome_HasCorrectClaims()
    {
        const string knownToken = "claude-code-cli-token";
        var validator = new FixedTokenValidator(knownToken);

        var host   = await BuildHostAsync(validator);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {knownToken}");

        var response = await client.GetAsync("/mcp");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        await host.StopAsync();
    }

    [Fact]
    public async Task InvalidToken_WithCompositeValidator_Returns401()
    {
        // Composite: static validator with a different token, OAuth validator rejects everything unknown
        var (oauthSvc, _, _, _, _, _) = OAuthServiceFactory.Create();
        var loggerFactory             = LoggerFactory.Create(_ => { });

        var staticOpts = Options.Create(new AtelierMcpOptions { Token = "the-real-token" });
        var userOpts   = Options.Create(new AtelierUserOptions { Username = "admin" });
        var staticValidator = new StaticTokenValidator(
            staticOpts,
            userOpts,
            loggerFactory.CreateLogger<StaticTokenValidator>());

        var oauthValidator  = new OAuthAccessTokenValidator(oauthSvc);
        var composite       = new CompositeTokenValidator(staticValidator, oauthValidator);

        var host   = await BuildHostAsync(composite);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer wrong-token");

        var response = await client.GetAsync("/mcp");

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);

        await host.StopAsync();
    }

    private sealed class FixedTokenValidator(string expectedToken) : ITokenValidator
    {
        public Task<TokenValidationOutcome> ValidateTokenAsync(string token, CancellationToken cancellationToken = default)
        {
            if (token == expectedToken)
                return Task.FromResult(new TokenValidationOutcome(true, "static-bearer", "static-client", null, null));
            return Task.FromResult(TokenValidationOutcome.Invalid);
        }
    }
}
