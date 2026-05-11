using Geef.Atelier.Tests.Persistence;
using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace Geef.Atelier.Tests.Web.E2E;

[Collection("Playwright")]
public sealed class LoginFlowTests(PlaywrightFixture pw, PostgresFixture pg) : IAsyncLifetime
{
    private WebTestHost? _host;

    public async Task InitializeAsync() =>
        _host = await WebTestHost.StartAsync(pg, initialGateCount: 100, authenticated: false);

    public async Task DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
    }

    [Fact]
    public async Task AnonymousAccess_Redirects_To_Login()
    {
        var page = await pw.Browser.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_host!.BaseUrl}/runs");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            Assert.Contains("/login", page.Url);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ShowsError()
    {
        var page = await pw.Browser.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_host!.BaseUrl}/login");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            await page.FillAsync("input#username", "wrong-user");
            await page.FillAsync("input#password", "wrong-password");
            await page.ClickAsync("button.btn-login");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            var errorEl = page.Locator(".login-error");
            await errorEl.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            var errorText = await errorEl.TextContentAsync();
            Assert.Contains("Ungültige Anmeldedaten", errorText);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task Login_WithValidCredentials_RedirectsToRuns()
    {
        var page = await pw.Browser.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_host!.BaseUrl}/login");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            await page.FillAsync("input#username", "test-user");
            await page.FillAsync("input#password", "test-password");
            await page.ClickAsync("button.btn-login");

            await page.WaitForURLAsync(new Regex(@"/runs"), new PageWaitForURLOptions { Timeout = 15_000 });

            Assert.Contains("/runs", page.Url);

            var userNameEl = page.Locator(".user-name");
            await userNameEl.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            var userName = await userNameEl.TextContentAsync();
            Assert.Contains("test-user", userName);
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
