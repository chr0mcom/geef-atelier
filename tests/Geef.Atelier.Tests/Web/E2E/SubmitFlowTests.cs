using Geef.Atelier.Tests.Persistence;
using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace Geef.Atelier.Tests.Web.E2E;

[Collection("Playwright")]
public sealed class SubmitFlowTests(PlaywrightFixture pw, PostgresFixture pg) : IAsyncLifetime
{
    private WebTestHost? _host;

    public async Task InitializeAsync() =>
        _host = await WebTestHost.StartAsync(pg, initialGateCount: 100);

    public async Task DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
    }

    [Fact]
    public async Task SubmitForm_RedirectsToRunDetail()
    {
        var page = await pw.Browser.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_host!.BaseUrl}/new");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // Fill and submit the form
            await page.FillAsync("textarea#briefing", "E2E test briefing for SubmitFlow");
            await page.ClickAsync("button[type='submit']");

            // Should redirect to /runs/{guid}
            await page.WaitForURLAsync(new Regex(@"/runs/[0-9a-f\-]{36}$"),
                new PageWaitForURLOptions { Timeout = 15_000 });

            // Page should show the run detail — status badge visible
            await page.WaitForSelectorAsync("[data-status]", new PageWaitForSelectorOptions { Timeout = 10_000 });

            var consoleErrors = new List<string>();
            page.Console += (_, msg) =>
            {
                if (msg.Type == "error") consoleErrors.Add(msg.Text);
            };

            Assert.Empty(consoleErrors);
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
