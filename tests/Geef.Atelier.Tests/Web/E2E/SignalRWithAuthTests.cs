using Geef.Atelier.Tests.Persistence;
using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace Geef.Atelier.Tests.Web.E2E;

[Collection("Playwright")]
public sealed class SignalRWithAuthTests(PlaywrightFixture pw, PostgresFixture pg) : IAsyncLifetime
{
    private WebTestHost? _host;

    public async Task InitializeAsync() =>
        _host = await WebTestHost.StartAsync(pg, initialGateCount: 100);

    public async Task DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
    }

    [Fact]
    public async Task LiveUpdate_WithAuthActive_ShowsCompletedStatus()
    {
        var page = await pw.Browser.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_host!.BaseUrl}/new");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            await page.FillAsync("textarea#briefing", "SignalR auth test briefing");
            await page.ClickAsync("button.btn-submit");

            await page.WaitForURLAsync(new Regex(@"/runs/[0-9a-f\-]{36}$"),
                new PageWaitForURLOptions { Timeout = 15_000 });

            await page.WaitForSelectorAsync("[data-status='Completed']",
                new PageWaitForSelectorOptions { Timeout = 60_000 });

            var statusEl = await page.QuerySelectorAsync("[data-status='Completed']");
            Assert.NotNull(statusEl);
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
