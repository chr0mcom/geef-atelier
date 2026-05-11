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

            // At least one RunCard should appear
            await page.WaitForSelectorAsync(".run-card", new PageWaitForSelectorOptions { Timeout = 10_000 });
            var cards = await page.QuerySelectorAllAsync(".run-card");
            Assert.True(cards.Count >= 1, $"Expected at least 1 run card, found {cards.Count}");

            // "Open →" link should point to /runs/{guid}
            var link = await cards[0].QuerySelectorAsync("a.run-card-link");
            Assert.NotNull(link);
            var href = await link!.GetAttributeAsync("href");
            Assert.Contains("/runs/", href);
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
