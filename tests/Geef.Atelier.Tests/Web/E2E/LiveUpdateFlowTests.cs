using Geef.Atelier.Tests.Persistence;
using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace Geef.Atelier.Tests.Web.E2E;

[Collection("Playwright")]
public sealed class LiveUpdateFlowTests(PlaywrightFixture pw, PostgresFixture pg) : IAsyncLifetime
{
    private WebTestHost? _host;

    public async Task InitializeAsync() =>
        // Start with gate open so pipeline can progress freely
        _host = await WebTestHost.StartAsync(pg, initialGateCount: 100);

    public async Task DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
    }

    [Fact]
    public async Task LiveUpdate_StatusReachesCompleted_WithoutPageReload()
    {
        var page = await pw.Browser.NewPageAsync();
        try
        {
            // Submit a run
            await page.GotoAsync($"{_host!.BaseUrl}/new");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await page.FillAsync("textarea#briefing", "Live update test briefing — pipeline should complete.");
            await page.ClickAsync("button[type='submit']");

            // Wait for redirect to detail page
            await page.WaitForURLAsync(new Regex(@"/runs/[0-9a-f\-]{36}$"),
                new PageWaitForURLOptions { Timeout = 15_000 });
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // Inject a JS marker that survives SignalR updates but NOT a page reload
            await page.EvaluateAsync("() => { window.__noReloadMarker = true; }");

            // Wait for Completed status — SignalR should deliver it without a page reload
            await page.WaitForSelectorAsync("[data-status='Completed']",
                new PageWaitForSelectorOptions { Timeout = 60_000 });

            // Verify the page was NOT reloaded (marker still present means no reload)
            var markerPresent = await page.EvaluateAsync<bool>("() => window.__noReloadMarker === true");
            Assert.True(markerPresent, "Expected SignalR live update without page reload, but the page was reloaded.");
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
