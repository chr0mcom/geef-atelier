using Geef.Atelier.Tests.Persistence;

namespace Geef.Atelier.Tests.Web.E2E;

[Collection("Playwright")]
public sealed class ThemeSwitcherE2ETests(PlaywrightFixture pw, PostgresFixture pg) : IAsyncLifetime
{
    private WebTestHost? _host;

    public async Task InitializeAsync() =>
        _host = await WebTestHost.StartAsync(pg, initialGateCount: 100, authenticated: false);

    public async Task DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
    }

    [Fact(Skip = "Requires JS-Interop; run manually against production")]
    public async Task ThemeSwitcher_PersistsThemeAcrossReload()
    {
        var page = await pw.Browser.NewPageAsync();
        try
        {
            // Login
            await page.GotoAsync($"{_host!.BaseUrl}/login");
            await page.FillAsync("input#username", "test-user");
            await page.FillAsync("input#password", "test-password");
            await page.ClickAsync("button.btn-login");
            await page.WaitForURLAsync($"{_host.BaseUrl}/");

            // Open UserMenu and switch to Noir
            await page.ClickAsync(".user-chip");
            await page.ClickAsync("[data-testid='theme-switcher-noir']");
            await page.WaitForFunctionAsync("document.documentElement.classList.contains('palette-noir')");

            // Reload and verify persistence
            await page.ReloadAsync();
            await page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle);

            var classes = await page.EvaluateAsync<string>("document.documentElement.className");
            Assert.Contains("palette-noir", classes);
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
