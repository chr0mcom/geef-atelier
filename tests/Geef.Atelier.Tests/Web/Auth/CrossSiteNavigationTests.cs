using System.Net;
using Geef.Atelier.Tests.EndToEnd;
using Geef.Atelier.Tests.Persistence;

namespace Geef.Atelier.Tests.Web.Auth;

/// <summary>
/// A page of another site opens a protected page of the Atelier. The sign-in cookie is <c>SameSite=Strict</c>, so the browser leaves
/// it out; instead of the login form the Atelier answers once with a page that opens the same address again from its own site, where
/// the browser sends the cookie. Everything else is answered as before.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class CrossSiteNavigationTests(PostgresFixture postgres)
{
    private const string Authorize = "/oauth/authorize?response_type=code&client_id=x&state=a%26b%22%3E%3Cscript%3E&code_challenge=c&code_challenge_method=S256";

    [Theory]
    [InlineData(Authorize)]
    [InlineData("/work")]
    [InlineData("/orders/0f8fad5b-d9cb-469f-a165-70867728950e?change=1")]
    public async Task ACrossSiteNavigationIsAnsweredOnceWithAReloadOfTheSameAddress(string address)
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        using var client = host.CreateAnonymousClient();

        using var first = await SendAsync(client, HttpMethod.Get, address, "cross-site", "navigate", "document");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("text/html", first.Content.Headers.ContentType!.MediaType);
        Assert.Contains("no-store", first.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        Assert.Contains(first.Headers.GetValues("Content-Security-Policy"), v => v.Contains("frame-ancestors 'self'", StringComparison.Ordinal));
        Assert.False(first.Headers.Contains("Set-Cookie"));
        var html = await first.Content.ReadAsStringAsync();
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        var target = TargetOf(html);
        var marked = address + (address.Contains('?') ? "&" : "?") + "same_site_reload=1";
        Assert.Equal(marked, target);

        using var again = await SendAsync(client, HttpMethod.Get, target, "cross-site", "navigate", "document");

        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
        Assert.StartsWith("/login?ReturnUrl=", again.Headers.Location!.PathAndQuery, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("same-origin", "navigate", "document")]
    [InlineData("same-site", "navigate", "document")]
    [InlineData("none", "navigate", "document")]
    [InlineData("cross-site", "navigate", "iframe")]
    [InlineData("cross-site", "cors", "empty")]
    [InlineData("cross-site", "no-cors", "image")]
    [InlineData(null, null, null)]
    public async Task EveryOtherRequestIsSentToTheLoginPageAsBefore(string? site, string? mode, string? destination)
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        using var client = host.CreateAnonymousClient();

        using var response = await SendAsync(client, HttpMethod.Get, "/work", site, mode, destination);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login?ReturnUrl=", response.Headers.Location!.PathAndQuery, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/oauth/consent")]
    [InlineData("/auth/logout")]
    public async Task ACrossSitePostIsNeverAnsweredWithAReload(string address)
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        using var client = host.CreateAnonymousClient();

        using var response = await SendAsync(client, HttpMethod.Post, address, "cross-site", "navigate", "document");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login", response.Headers.Location!.PathAndQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiAddressesStillAnswerWithAStatusCode()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        using var client = host.CreateAnonymousClient();

        using var response = await SendAsync(client, HttpMethod.Get, $"/api/sources/{Guid.NewGuid()}", "cross-site", "navigate", "document");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string address, string? site, string? mode, string? destination)
    {
        using var request = new HttpRequestMessage(method, address);
        foreach (var (name, value) in new[] { ("Sec-Fetch-Site", site), ("Sec-Fetch-Mode", mode), ("Sec-Fetch-Dest", destination) })
        {
            if (value is not null)
                request.Headers.Add(name, value);
        }

        return await client.SendAsync(request);
    }

    private static string TargetOf(string html)
    {
        const string start = "<meta http-equiv=\"refresh\" content=\"0;url=";
        var from = html.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, "The page holds no reload.");
        from += start.Length;
        return WebUtility.HtmlDecode(html[from..html.IndexOf('"', from)]);
    }
}
