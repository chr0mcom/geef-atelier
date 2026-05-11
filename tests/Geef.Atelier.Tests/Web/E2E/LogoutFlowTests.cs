using Geef.Atelier.Tests.Persistence;
using Microsoft.Playwright;

namespace Geef.Atelier.Tests.Web.E2E;

[Collection("Playwright")]
public sealed class LogoutFlowTests(PlaywrightFixture pw, PostgresFixture pg) : IAsyncLifetime
{
    private WebTestHost? _host;

    public async Task InitializeAsync() =>
        _host = await WebTestHost.StartAsync(pg, initialGateCount: 100, authenticated: false);

    public async Task DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
    }

    private async Task LoginAsync(IPage page)
    {
        await page.GotoAsync($"{_host!.BaseUrl}/login");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.FillAsync("input#username", "test-user");
        await page.FillAsync("input#password", "test-password");
        await page.ClickAsync("button.btn-login");
        await page.WaitForURLAsync("**/runs**", new PageWaitForURLOptions { Timeout = 15_000 });
    }

    [Fact]
    public async Task Logout_ClearsSession_And_Redirects_To_Login()
    {
        var page = await pw.Browser.NewPageAsync();
        try
        {
            await LoginAsync(page);
            Assert.Contains("/runs", page.Url);

            await page.ClickAsync("button.btn-logout");
            await page.WaitForURLAsync("**/login**", new PageWaitForURLOptions { Timeout = 15_000 });

            Assert.Contains("/login", page.Url);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task AfterLogout_AccessingAuthPage_RedirectsToLogin()
    {
        var page = await pw.Browser.NewPageAsync();
        try
        {
            await LoginAsync(page);
            Assert.Contains("/runs", page.Url);

            await page.ClickAsync("button.btn-logout");
            await page.WaitForURLAsync("**/login**", new PageWaitForURLOptions { Timeout = 15_000 });

            await page.GotoAsync($"{_host!.BaseUrl}/runs");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            Assert.Contains("/login", page.Url);
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
