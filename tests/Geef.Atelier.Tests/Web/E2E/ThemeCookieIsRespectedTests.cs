using System.Net;
using Geef.Atelier.Tests.Persistence;

namespace Geef.Atelier.Tests.Web.E2E;

[Collection("Postgres")]
public sealed class ThemeCookieIsRespectedTests(PostgresFixture pg) : IAsyncLifetime
{
    private WebTestHost? _host;

    public async Task InitializeAsync() =>
        _host = await WebTestHost.StartAsync(pg, initialGateCount: 100, authenticated: true);

    public async Task DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
    }

    [Theory]
    [InlineData("noir",   "palette-noir")]
    [InlineData("petrol", "palette-petrol")]
    [InlineData("vellum", "palette-vellum")]
    public async Task ThemeCookie_IsReflectedInHtmlClass(string cookieValue, string expectedClass)
    {
        var cookieContainer = new CookieContainer();
        cookieContainer.Add(new Uri(_host!.BaseUrl), new Cookie("Atelier.Theme", cookieValue));

        using var handler = new HttpClientHandler { CookieContainer = cookieContainer };
        using var client  = new HttpClient(handler);

        var response = await client.GetAsync($"{_host!.BaseUrl}/");
        var html     = await response.Content.ReadAsStringAsync();

        Assert.Contains(expectedClass, html);
    }
}
