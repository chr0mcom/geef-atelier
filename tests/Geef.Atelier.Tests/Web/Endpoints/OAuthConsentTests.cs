using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Tests.EndToEnd;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Tests.Web.Uploads;
using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Endpoints;

/// <summary>
/// The consent form over the real <c>Program</c> wiring: it carries an antiforgery token that the server checks, the sign-in flow of
/// an OAuth client (authorize, sign in, consent, redirect, code exchange) works for "Grant access" and "Deny", and the page cannot be
/// shown in a frame of another site.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed partial class OAuthConsentTests(PostgresFixture postgres)
{
    private const string RedirectUri = "http://127.0.0.1/consent-test/callback";
    private const string TokenField = "__RequestVerificationToken";
    private const string Password = "consent-test-password";

    [Fact]
    public async Task AConsentPostOfASignedInPersonWithoutATokenIsRefused()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var clientId = await RegisterAsync(host);
        using var person = await host.CreateSignedInClientAsync(Name("person"));

        using var response = await person.PostAsync("/oauth/consent", new FormUrlEncodedContent(Fields(clientId, "approve", Challenge(Verifier()))));

        await AssertRefusedAsync(response);
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("deny")]
    public async Task AWrongTokenOrTheTokenOfAnotherPersonIsRefused(string action)
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var clientId = await RegisterAsync(host);
        using var person = await SignInAsync(host, Name("person"));
        using var other = await SignInAsync(host, Name("other"));
        var own = await ConsentFormAsync(person, clientId, Challenge(Verifier()));
        var foreign = await ConsentFormAsync(other, clientId, Challenge(Verifier()));

        using (var wrong = await PostConsentAsync(person, own, action, token: "not-a-token"))
            await AssertRefusedAsync(wrong);
        using (var otherPersons = await PostConsentAsync(person, own, action, token: foreign[TokenField]))
            await AssertRefusedAsync(otherPersons);
    }

    [Fact]
    public async Task SignInConsentAndCodeExchangeWorkForAPersonWhoStartsSignedOut()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var clientId = await RegisterAsync(host);
        var username = Name("person");
        await host.CreateUserWithPasswordAsync(username, Password);
        using var browser = host.CreateBrowserClient();
        var verifier = Verifier();
        var authorize = AuthorizePath(clientId, Challenge(verifier), "state-1");

        using var challenge = await browser.GetAsync(authorize);
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var loginPath = challenge.Headers.Location!.PathAndQuery;
        Assert.StartsWith("/login?ReturnUrl=", loginPath, StringComparison.Ordinal);

        using var signedIn = await LogInAsync(browser, loginPath, username);
        Assert.Equal(authorize, signedIn.Headers.Location!.OriginalString);

        var form = await ConsentFormAsync(browser, signedIn.Headers.Location.OriginalString);
        using var approved = await PostConsentAsync(browser, form, "approve");

        Assert.Equal(HttpStatusCode.Redirect, approved.StatusCode);
        var callback = approved.Headers.Location!.OriginalString;
        Assert.StartsWith(RedirectUri + "?code=", callback, StringComparison.Ordinal);
        Assert.EndsWith("&state=state-1", callback, StringComparison.Ordinal);
        var code = Uri.UnescapeDataString(callback.Split("code=")[1].Split('&')[0]);

        using var anonymous = host.CreateAnonymousClient();
        using var exchange = await anonymous.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = clientId,
            ["redirect_uri"] = RedirectUri,
            ["code_verifier"] = verifier,
        }));
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
    }

    [Fact]
    public async Task DenyLeadsBackToTheClientWithAccessDenied()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var clientId = await RegisterAsync(host);
        using var browser = await SignInAsync(host, Name("person"));
        var form = await ConsentFormAsync(browser, clientId, Challenge(Verifier()));

        using var denied = await PostConsentAsync(browser, form, "deny");

        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal($"{RedirectUri}?error=access_denied&state=state-1", denied.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task TheConsentPageCannotBeShownInAFrameOfAnotherSite()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var clientId = await RegisterAsync(host);
        using var browser = await SignInAsync(host, Name("person"));

        using var page = await browser.GetAsync(AuthorizePath(clientId, Challenge(Verifier()), "state-1"));

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains(page.Headers.GetValues("Content-Security-Policy"), v => v.Contains("frame-ancestors 'self'", StringComparison.Ordinal));
    }

    /// <summary>
    /// Every endpoint outside the Razor components that changes something, with the reason why it is safe. A new one fails this
    /// test until it is checked and listed here. Razor component forms are checked by the framework.
    /// </summary>
    private static readonly Dictionary<string, string> StateChangingEndpoints = new(StringComparer.Ordinal)
    {
        ["POST /oauth/consent"] = "form token, checked in the handler",
        ["POST /auth/logout"] = "form token, checked in the handler",
        ["POST /settings/theme"] = "form token, checked by the middleware (form binding)",
        ["POST /api/sources"] = "header token and Origin, checked in the handler (the body is read as a stream)",
        ["DELETE /api/sources/{sourceId:guid}"] = "header token and Origin, checked in the handler",
        ["POST /api/uploads/{uploadId:guid}/page"] = "header token and Origin, checked in the handler (the body is read as a stream)",
        ["PUT /api/uploads/{uploadId:guid}"] = "no cookie: one-time token in a header",
        ["POST /oauth/register"] = "no cookie: anonymous client registration",
        ["POST /oauth/token"] = "no cookie: code with PKCE verifier or refresh token",
        ["POST /oauth/revoke"] = "no cookie: needs the token itself",
    };

    [Fact]
    public async Task EveryStateChangingEndpointIsListedWithItsProtection()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres,
            services => services.AddSingleton<IUploadTicketService>(new FakeUploadTickets()));

        var mapped = host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<ComponentTypeMetadata>() is null)
            .Select(e => (Route: "/" + e.RoutePattern.RawText!.Trim('/'), Methods: e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []))
            .Where(e => !e.Route.StartsWith("/mcp", StringComparison.Ordinal) && !e.Route.StartsWith("/_", StringComparison.Ordinal))
            .SelectMany(e => e.Methods.Where(m => m is "POST" or "PUT" or "PATCH" or "DELETE").Select(m => $"{m} {e.Route}"))
            .Distinct()
            .Order(StringComparer.Ordinal);

        Assert.Equal(StateChangingEndpoints.Keys.Order(StringComparer.Ordinal), mapped);
    }

    [Fact]
    public async Task TheMcpEndpointDoesNotTakeTheCookieSignIn()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        using var browser = await SignInAsync(host, Name("person"));

        using var response = await browser.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SigningOutWorksWithTheTokenOfThePage()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var clientId = await RegisterAsync(host);
        using var browser = await SignInAsync(host, Name("person"));
        var form = await ConsentFormAsync(browser, clientId, Challenge(Verifier()));

        using var signedOut = await browser.PostAsync("/auth/logout",
            new FormUrlEncodedContent(new Dictionary<string, string> { [TokenField] = form[TokenField] }));

        Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
        Assert.Equal("/login", signedOut.Headers.Location!.OriginalString);
        using var afterwards = await browser.GetAsync("/start");
        Assert.Equal(HttpStatusCode.Redirect, afterwards.StatusCode);
    }

    [Fact]
    public async Task SigningOutNeedsTheTokenOfThePage()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        using var browser = await SignInAsync(host, Name("person"));

        using (var without = await browser.PostAsync("/auth/logout", new FormUrlEncodedContent([])))
            Assert.Equal(HttpStatusCode.BadRequest, without.StatusCode);
        using (var stillSignedIn = await browser.GetAsync("/start"))
            Assert.Equal(HttpStatusCode.OK, stillSignedIn.StatusCode);
    }

    [Fact]
    public async Task TheThemeFallbackNeedsATokenToo()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        using var browser = await SignInAsync(host, Name("person"));

        using var without = await browser.PostAsync("/settings/theme", new FormUrlEncodedContent(new Dictionary<string, string> { ["value"] = "noir" }));

        Assert.Equal(HttpStatusCode.BadRequest, without.StatusCode);
    }

    [Theory]
    [InlineData("https://other.localhost", false)]
    [InlineData("null", false)]
    [InlineData("http://localhost", true)]
    [InlineData(null, true)]
    public async Task TheLiveConnectionOnlyTakesPagesOfThisHost(string? origin, bool allowed)
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        using var browser = await SignInAsync(host, Name("person"));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/_blazor/negotiate?negotiateVersion=1");
        if (origin is not null)
            request.Headers.Add("Origin", origin);

        using var response = await browser.SendAsync(request);

        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static string Name(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    private static string Verifier() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static string Challenge(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string AuthorizePath(string clientId, string challenge, string state) =>
        $"/oauth/authorize?response_type=code&client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
        $"&scope=mcp%3Afull&state={state}&code_challenge={challenge}&code_challenge_method=S256";

    private static Dictionary<string, string> Fields(string clientId, string action, string challenge) => new()
    {
        ["action"] = action,
        ["response_type"] = "code",
        ["client_id"] = clientId,
        ["redirect_uri"] = RedirectUri,
        ["scope"] = "mcp:full",
        ["state"] = "state-1",
        ["code_challenge"] = challenge,
        ["code_challenge_method"] = "S256",
    };

    private static async Task<string> RegisterAsync(AtelierEndToEndHost host)
    {
        using var client = host.CreateAnonymousClient();
        using var response = await client.PostAsJsonAsync("/oauth/register", new { client_name = "Consent test", redirect_uris = new[] { RedirectUri } });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("client_id").GetString()!;
    }

    private static async Task<HttpClient> SignInAsync(AtelierEndToEndHost host, string username)
    {
        await host.CreateUserWithPasswordAsync(username, Password);
        var browser = host.CreateBrowserClient();
        using var signedIn = await LogInAsync(browser, "/login", username);
        return browser;
    }

    private static async Task<HttpResponseMessage> LogInAsync(HttpClient browser, string loginPath, string username)
    {
        var page = await browser.GetStringAsync(loginPath);
        var fields = InputsOf(page);
        fields["_handler"] = "login-form";
        fields["Model.Username"] = username;
        fields["Model.Password"] = Password;
        var response = await browser.PostAsync(loginPath, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response;
    }

    private static Task<Dictionary<string, string>> ConsentFormAsync(HttpClient browser, string clientId, string challenge) =>
        ConsentFormAsync(browser, AuthorizePath(clientId, challenge, "state-1"));

    private static async Task<Dictionary<string, string>> ConsentFormAsync(HttpClient browser, string authorizePath)
    {
        var page = await browser.GetStringAsync(authorizePath);
        var form = ConsentFormPattern().Match(page);
        Assert.True(form.Success, "The page holds no consent form.");
        return InputsOf(form.Value);
    }

    private static Dictionary<string, string> InputsOf(string html) =>
        InputPattern().Matches(html)
            .Select(tag => (Name: Attribute(tag.Value, "name"), Value: Attribute(tag.Value, "value"), Type: Attribute(tag.Value, "type")))
            .Where(i => i.Name is not null && i.Type == "hidden")
            .ToDictionary(i => i.Name!, i => i.Value ?? string.Empty);

    private static string? Attribute(string tag, string name)
    {
        var match = Regex.Match(tag, $"\\s{name}=\"([^\"]*)\"");
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
    }

    private static Task<HttpResponseMessage> PostConsentAsync(HttpClient browser, Dictionary<string, string> form, string action, string? token = null)
    {
        var fields = new Dictionary<string, string>(form) { ["action"] = action };
        if (token is not null)
            fields[TokenField] = token;
        return browser.PostAsync("/oauth/consent", new FormUrlEncodedContent(fields));
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response)
    {
        var location = response.Headers.Location?.OriginalString ?? string.Empty;
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"Expected 400, got {(int)response.StatusCode} {location.Split('=')[0]}");
        Assert.Null(response.Headers.Location);
        Assert.Equal("This page is out of date. Go back, reload it and try again.", await response.Content.ReadAsStringAsync());
    }

    [GeneratedRegex("<form[^>]*action=\"/oauth/consent\".*?</form>", RegexOptions.Singleline)]
    private static partial Regex ConsentFormPattern();

    [GeneratedRegex("<input[^>]*>")]
    private static partial Regex InputPattern();
}
