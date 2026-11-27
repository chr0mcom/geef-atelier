using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Geef.Atelier.Tests.EndToEnd;
using Geef.Atelier.Tests.Persistence;
using Microsoft.Playwright;

namespace Geef.Atelier.Tests.Web.E2E;

/// <summary>
/// The sign-in of an OAuth client in a real browser against the real <c>Program</c> on Kestrel. First a page of another site opens
/// <c>/oauth/authorize</c> for a signed-out person, who signs in, grants and is sent back to the client. Then the signed-in person
/// opens the address directly (as when a desktop or command line client opens the browser) and denies. At the end the person signs
/// out through the user menu after a navigation without a page load.
/// </summary>
[Collection("Playwright")]
public sealed class OAuthConsentBrowserFlowTests(PlaywrightFixture pw, PostgresFixture pg)
{
    private const string ClientPage = "https://client.example.test/connect";
    private const string Password = "browser-test-password";

    [Fact]
    public async Task GrantAndDenyLeadBackToTheClientAndSigningOutStillWorks()
    {
        var port = FreePort();
        var baseUrl = $"http://127.0.0.1:{port}";
        var redirectUri = $"http://localhost:{FreePort()}/callback";
        using var client = ListenForCallbacks(redirectUri);
        await using var host = await AtelierEndToEndHost.StartAsync(pg, kestrelPort: port);
        var username = $"browser-{Guid.NewGuid():N}"[..20];
        await host.CreateUserWithPasswordAsync(username, Password);
        var clientId = await RegisterAsync(baseUrl, redirectUri);
        await using var context = await pw.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        await OpenFromAnotherSiteAsync(page, AuthorizeUrl(baseUrl, clientId, redirectUri, "first"));
        await page.FillAsync("input#username", username);
        await page.FillAsync("input#password", Password);
        await page.ClickAsync("button.btn-login");
        var granted = await AnswerAsync(page, redirectUri, "btn-approve");
        Assert.StartsWith(redirectUri + "?code=", granted, StringComparison.Ordinal);
        Assert.EndsWith("&state=first", granted, StringComparison.Ordinal);

        await page.GotoAsync(AuthorizeUrl(baseUrl, clientId, redirectUri, "second"));
        var denied = await AnswerAsync(page, redirectUri, "btn-deny");
        Assert.Equal($"{redirectUri}?error=access_denied&state=second", denied);

        await page.GotoAsync($"{baseUrl}/start");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.ClickAsync(".nav-links a.nav-link[href='/work']");
        await page.WaitForURLAsync("**/work", new PageWaitForURLOptions { Timeout = 15_000 });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.ClickAsync("button.user-chip");
        await page.ClickAsync("button.btn-logout");
        await page.WaitForURLAsync("**/login**", new PageWaitForURLOptions { Timeout = 15_000 });

        await page.GotoAsync($"{baseUrl}/start");
        Assert.Contains("/login", page.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASignedInPersonComingFromAnotherSiteSeesTheConsentPageAndNoLoginForm()
    {
        var port = FreePort();
        var baseUrl = $"http://127.0.0.1:{port}";
        var redirectUri = $"http://localhost:{FreePort()}/callback";
        using var client = ListenForCallbacks(redirectUri);
        await using var host = await AtelierEndToEndHost.StartAsync(pg, kestrelPort: port);
        var username = $"browser-{Guid.NewGuid():N}"[..20];
        await host.CreateUserWithPasswordAsync(username, Password);
        var clientId = await RegisterAsync(baseUrl, redirectUri);
        await using var context = await pw.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await SignInAsync(page, baseUrl, username);
        var loginPages = 0;
        page.Request += (_, request) => loginPages += request.Url.StartsWith($"{baseUrl}/login", StringComparison.Ordinal) ? 1 : 0;

        await OpenFromAnotherSiteAsync(page, AuthorizeUrl(baseUrl, clientId, redirectUri, "first"));
        var granted = await AnswerAsync(page, redirectUri, "btn-approve");
        await OpenFromAnotherSiteAsync(page, AuthorizeUrl(baseUrl, clientId, redirectUri, "second"));
        var denied = await AnswerAsync(page, redirectUri, "btn-deny");
        await OpenFromAnotherSiteAsync(page, $"{baseUrl}/work");
        await page.Locator("button.user-chip").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

        Assert.StartsWith(redirectUri + "?code=", granted, StringComparison.Ordinal);
        Assert.EndsWith("&state=first", granted, StringComparison.Ordinal);
        Assert.Equal($"{redirectUri}?error=access_denied&state=second", denied);
        Assert.StartsWith($"{baseUrl}/work", page.Url, StringComparison.Ordinal);
        Assert.Equal(0, loginPages);
    }

    [Fact]
    public async Task ASignedOutPersonComingFromAnotherSiteIsAskedToSignInOnceAndThenGrants()
    {
        var port = FreePort();
        var baseUrl = $"http://127.0.0.1:{port}";
        var redirectUri = $"http://localhost:{FreePort()}/callback";
        using var client = ListenForCallbacks(redirectUri);
        await using var host = await AtelierEndToEndHost.StartAsync(pg, kestrelPort: port);
        var username = $"browser-{Guid.NewGuid():N}"[..20];
        await host.CreateUserWithPasswordAsync(username, Password);
        var clientId = await RegisterAsync(baseUrl, redirectUri);
        await using var context = await pw.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var asked = new List<string>();
        page.Request += (_, request) =>
        {
            if (request.IsNavigationRequest && request.Url.StartsWith(baseUrl, StringComparison.Ordinal))
                asked.Add($"{request.Method} {new Uri(request.Url).AbsolutePath}");
        };

        await OpenFromAnotherSiteAsync(page, AuthorizeUrl(baseUrl, clientId, redirectUri, "first"));
        await page.FillAsync("input#username", username);
        await page.FillAsync("input#password", Password);
        await page.ClickAsync("button.btn-login");
        var granted = await AnswerAsync(page, redirectUri, "btn-approve");

        Assert.StartsWith(redirectUri + "?code=", granted, StringComparison.Ordinal);
        Assert.InRange(asked.Count(a => a == "GET /oauth/authorize"), 2, 3);
        Assert.Single(asked, a => a == "GET /login");
        Assert.Single(asked, a => a == "POST /login");
    }

    private static async Task SignInAsync(IPage page, string baseUrl, string username)
    {
        await page.GotoAsync($"{baseUrl}/login");
        await page.FillAsync("input#username", username);
        await page.FillAsync("input#password", Password);
        await page.ClickAsync("button.btn-login");
        await page.WaitForURLAsync("**/start", new PageWaitForURLOptions { Timeout = 15_000 });
    }

    private static async Task OpenFromAnotherSiteAsync(IPage page, string authorizeUrl)
    {
        await page.RouteAsync(ClientPage, route => route.FulfillAsync(new RouteFulfillOptions
        {
            ContentType = "text/html",
            Body = $"<a id=\"connect\" href=\"{WebUtility.HtmlEncode(authorizeUrl)}\">Connect</a>",
        }));
        await page.GotoAsync(ClientPage);
        await page.ClickAsync("#connect");
    }

    private static async Task<string> AnswerAsync(IPage page, string redirectUri, string button)
    {
        await page.Locator($"[data-testid='{button}']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        var callback = await page.RunAndWaitForRequestAsync(
            () => page.ClickAsync($"[data-testid='{button}']"),
            request => request.Url.StartsWith(redirectUri, StringComparison.Ordinal),
            new PageRunAndWaitForRequestOptions { Timeout = 15_000 });
        await page.WaitForURLAsync(callback.Url, new PageWaitForURLOptions { Timeout = 15_000 });
        return callback.Url;
    }

    private static HttpListener ListenForCallbacks(string redirectUri)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add(new Uri(redirectUri).GetLeftPart(UriPartial.Authority) + "/");
        listener.Start();
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                try
                {
                    (await listener.GetContextAsync()).Response.Close("Connected"u8.ToArray(), willBlock: false);
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
                {
                    return;
                }
            }
        });
        return listener;
    }

    private static string AuthorizeUrl(string baseUrl, string clientId, string redirectUri, string state) =>
        $"{baseUrl}/oauth/authorize?response_type=code&client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
        $"&scope=mcp%3Afull&state={state}&code_challenge=Oaa_K782ehJ6ZNf-INVXFk1mEKtzQz7xOERSXZUiGXA&code_challenge_method=S256";

    private static async Task<string> RegisterAsync(string baseUrl, string redirectUri)
    {
        using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
        using var response = await client.PostAsJsonAsync("/oauth/register", new { client_name = "Browser test", redirect_uris = new[] { redirectUri } });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("client_id").GetString()!;
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
