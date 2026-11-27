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
        // Create a run, then open the list
        var page = await pw.Browser.NewPageAsync();
        try
        {
            var runId = await _host!.CreateRunAsync("List flow test briefing");
            await page.GotoAsync($"{_host.BaseUrl}/runs/{runId}");

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
