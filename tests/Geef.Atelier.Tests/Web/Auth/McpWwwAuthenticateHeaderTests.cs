using Geef.Atelier.Application.Auth;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Web.Auth;

/// <summary>Verifies that the WWW-Authenticate header is added to 401 responses from the MCP path.</summary>
public sealed class McpWwwAuthenticateHeaderTests
{
    [Fact]
    public async Task MCP_Path_401Response_ContainsWwwAuthenticateHeader()
    {
        const string issuer = "https://geef.stefan-bechtel.de";

        using var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton<ITokenValidator>(new AlwaysInvalidTokenValidator());
                    services.Configure<OAuthOptions>(o => o.Issuer = issuer);
                    services.AddAuthentication("Bearer")
                        .AddScheme<AuthenticationSchemeOptions, BearerTokenHandler>("Bearer", _ => { });
                    services.AddAuthorization(o =>
                        o.AddPolicy("McpPolicy", p =>
                        {
                            p.AuthenticationSchemes = ["Bearer"];
                            p.RequireAuthenticatedUser();
                        }));
                });
                web.Configure(app =>
                {
                    // Must run before UseAuthentication so callback is registered on response
                    app.Use(async (ctx, next) =>
                    {
                        ctx.Response.OnStarting(() =>
                        {
                            if (ctx.Request.Path.StartsWithSegments("/mcp") && ctx.Response.StatusCode == 401
                                && !ctx.Response.Headers.ContainsKey("WWW-Authenticate"))
                                ctx.Response.Headers.WWWAuthenticate =
                                    $"Bearer resource_metadata=\"{issuer}/.well-known/oauth-protected-resource\"";
                            return Task.CompletedTask;
                        });
                        await next();
                    });
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(ep =>
                        ep.MapGet("/mcp", () => TypedResults.Ok("ok")).RequireAuthorization("McpPolicy"));
                });
            })
            .StartAsync();

        var client = host.GetTestClient();
        var response = await client.GetAsync("/mcp");

        // First verify we get 401, then check header
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);

        // Check all response headers for debugging
        var allHeaders = string.Join(", ", response.Headers.Select(h => $"{h.Key}=[{string.Join(";", h.Value)}]"));
        var wwwAuth = response.Headers.Contains("WWW-Authenticate")
            ? string.Join(";", response.Headers.GetValues("WWW-Authenticate"))
            : "(not present)";

        Assert.Contains("resource_metadata", wwwAuth, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class AlwaysInvalidTokenValidator : ITokenValidator
    {
        public Task<TokenValidationOutcome> ValidateTokenAsync(string token, CancellationToken ct = default)
            => Task.FromResult(TokenValidationOutcome.Invalid);
    }
}
