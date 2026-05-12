using Geef.Atelier.Tests.Persistence;

namespace Geef.Atelier.Tests.Web.E2E;

[Collection("Postgres")]
public sealed class ThemeCookieDefaultsToVellumTests(PostgresFixture pg) : IAsyncLifetime
{
    private WebTestHost? _host;

    public async Task InitializeAsync() =>
        _host = await WebTestHost.StartAsync(pg, initialGateCount: 100, authenticated: true);

    public async Task DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
    }

    [Fact]
    public async Task NoThemeCookie_RendersHtmlWithPaletteVellum()
    {
        using var client = new HttpClient();
        var response = await client.GetAsync($"{_host!.BaseUrl}/");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("palette-vellum", html);
    }
}
