using Geef.Atelier.Application.Auth;
using Geef.Atelier.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Geef.Atelier.Tests.Web.Auth;

public sealed class BearerTokenHandlerDoesNotInterfereWithCookieAuthTests
{
    [Fact]
    public async Task RouteWithoutMcpPolicy_AllowsAnonymousWithoutBearerHeader()
    {
        // Verify that registering the Bearer scheme does not break anonymous routes.
        // When no policy is applied (AllowAnonymous), requests without Bearer header
        // are served correctly — the Bearer handler's NoResult does not cause 401.
        var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddSingleton<ITokenValidator>(new AlwaysValidTokenValidator());
                    services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                        .AddCookie()
                        .AddScheme<AuthenticationSchemeOptions, BearerTokenHandler>("Bearer", _ => { });
                    services.AddAuthorization(o =>
                        o.AddPolicy("McpPolicy", p =>
                        {
                            p.AuthenticationSchemes = new[] { "Bearer" };
                            p.RequireAuthenticatedUser();
                        }));
                    services.AddRouting();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(ep =>
                    {
                        // This route does NOT require McpPolicy — it should be reachable
                        // without any Authorization header even when Bearer scheme is registered.
                        ep.MapGet("/public", () => "public-ok").AllowAnonymous();

                        // MCP route remains protected
                        ep.MapGet("/mcp-protected", () => "mcp-ok").RequireAuthorization("McpPolicy");
                    });
                });
            })
            .StartAsync();

        var client = host.GetTestClient();

        // No Authorization header — public route must return 200
        var publicResponse = await client.GetAsync("/public");
        Assert.Equal(System.Net.HttpStatusCode.OK, publicResponse.StatusCode);

        // No Authorization header — MCP-protected route must return 401
        var mcpResponse = await client.GetAsync("/mcp-protected");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, mcpResponse.StatusCode);

        await host.StopAsync();
    }

    private sealed class AlwaysValidTokenValidator : ITokenValidator
    {
        public Task<bool> ValidateTokenAsync(string token, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }
}
