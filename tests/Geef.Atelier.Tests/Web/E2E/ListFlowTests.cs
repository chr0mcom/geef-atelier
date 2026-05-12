using Geef.Atelier.Tests.Persistence;
using Microsoft.Playwright;

namespace Geef.Atelier.Tests.Web.E2E;

[Collection("Playwright")]
public sealed class ListFlowTests(PlaywrightFixture pw, PostgresFixture pg) : IAsyncLifetime
{
    private WebTestHost? _host;

    public async Task InitializeAsync() =>
        _host = await WebTestHost.StartAsync(pg, initialGateCount: 100);

    public async Task DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
    }

    [Fact]
    public async Task RunsList_ShowsSubmittedRuns()
    {
        // Submit a run via the UI
        var page = await pw.Browser.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_host!.BaseUrl}/new");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            await page.FillAsync("textarea#briefing", "List flow test briefing");
            await page.ClickAsync("button.btn-submit");

            // Wait for redirect to detail page
            await page.WaitForURLAsync("**/runs/**", new PageWaitForURLOptions { Timeout = 10_000 });

            // Navigate to list
            await page.GotoAsync($"{_host.BaseUrl}/runs");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // At least one RunRow should appear
            await page.WaitForSelectorAsync(".run-row", new PageWaitForSelectorOptions { Timeout = 10_000 });
            var rows = await page.QuerySelectorAllAsync(".run-row");
            Assert.True(rows.Count >= 1, $"Expected at least 1 run row, found {rows.Count}");
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
