using Geef.Atelier.Application.Auth;
using Geef.Atelier.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Geef.Atelier.Tests.Web.Auth;

public sealed class BearerTokenHandlerRejectsMissingTokenTests
{
    [Fact]
    public async Task MissingAuthorizationHeader_Returns401()
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddSingleton<ITokenValidator>(new AlwaysValidTokenValidator());
                    services.AddAuthentication()
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
                    app.UseEndpoints(ep => ep.MapGet("/test", () => "ok").RequireAuthorization("McpPolicy"));
                });
            })
            .StartAsync();

        // Send request WITHOUT any Authorization header
        var client = host.GetTestClient();
        var response = await client.GetAsync("/test");

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);

        await host.StopAsync();
    }

    private sealed class AlwaysValidTokenValidator : ITokenValidator
    {
        public Task<TokenValidationOutcome> ValidateTokenAsync(string token, CancellationToken cancellationToken = default)
            => Task.FromResult(new TokenValidationOutcome(true, "static-bearer", "static-client", null, null));
    }
}
