using System.Net;
using Geef.Atelier.Web.Auth;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Geef.Atelier.Tests.Web.Auth;

/// <summary>Challenges and forbids under <c>/api</c> answer with 401/403; every other path keeps the redirect.</summary>
public sealed class ApiCookieEventsTests
{
    [Theory]
    [InlineData("/api/challenge", HttpStatusCode.Unauthorized)]
    [InlineData("/API/challenge", HttpStatusCode.Unauthorized)]
    [InlineData("/api/forbid", HttpStatusCode.Forbidden)]
    public async Task Api_paths_get_a_status_code_instead_of_a_redirect(string path, HttpStatusCode expected)
    {
        using var host = await StartAsync();
        using var client = host.GetTestClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(expected, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData("/page/challenge", "/login")]
    [InlineData("/page/forbid", "/access-denied")]
    [InlineData("/apiary/challenge", "/login")]
    public async Task Other_paths_keep_the_redirect(string path, string target)
    {
        using var host = await StartAsync();
        using var client = host.GetTestClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(target, response.Headers.Location?.AbsolutePath, StringComparison.Ordinal);
    }

    private static Task<IHost> StartAsync() =>
        new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                        .AddCookie(o =>
                        {
                            o.LoginPath = "/login";
                            o.AccessDeniedPath = "/access-denied";
                            o.Events.AnswerApiWithStatusCodes();
                        });
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseEndpoints(endpoints =>
                    {
                        foreach (var prefix in new[] { "/api", "/page", "/apiary" })
                        {
                            endpoints.MapGet($"{prefix}/challenge", () => Results.Challenge());
                            endpoints.MapGet($"{prefix}/forbid", () => Results.Forbid());
                        }
                    });
                });
            })
            .StartAsync();
}
