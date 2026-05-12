using Geef.Atelier.Tests.Persistence;
using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace Geef.Atelier.Tests.Web.E2E;

[Collection("Playwright")]
public sealed class CancelFlowTests(PlaywrightFixture pw, PostgresFixture pg) : IAsyncLifetime
{
    private WebTestHost? _host;

    public async Task InitializeAsync() =>
        // Start with gate closed (0 permits) so pipeline stalls in Running state
        _host = await WebTestHost.StartAsync(pg, initialGateCount: 0);

    public async Task DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
    }

    [Fact]
    public async Task CancelButton_SetsStatusToAborted()
    {
        var page = await pw.Browser.NewPageAsync();
        try
        {
            // Submit a run — pipeline will stall because gate is closed
            await page.GotoAsync($"{_host!.BaseUrl}/new");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await page.FillAsync("textarea#briefing", "Cancel flow test — this pipeline will be cancelled.");
            await page.ClickAsync("button.btn-submit");

            // Wait for redirect to detail page
            await page.WaitForURLAsync(new Regex(@"/runs/[0-9a-f\-]{36}$"),
                new PageWaitForURLOptions { Timeout = 15_000 });
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // Wait for either Pending or Running status (pipeline stalled at gate)
            await page.WaitForSelectorAsync("[data-status='Pending'], [data-status='Running']",
                new PageWaitForSelectorOptions { Timeout = 15_000 });

            // Click Cancel button
            var cancelBtn = await page.WaitForSelectorAsync("button.btn-cancel:not(:disabled)",
                new PageWaitForSelectorOptions { Timeout = 10_000 });
            Assert.NotNull(cancelBtn);
            await cancelBtn!.ClickAsync();

            // Do NOT release the gate here — WatchCancellationAsync cancels the CTS within 200ms,
            // and gate.WaitAsync(ct) with a cancelled token throws OCE immediately without needing a permit.
            // Releasing the gate would let the FakeLlmClient race to Completed before the CTS is cancelled.

            // Wait for Aborted status — should arrive via SignalR within ~500ms
            await page.WaitForSelectorAsync("[data-status='Aborted']",
                new PageWaitForSelectorOptions { Timeout = 30_000 });
        }
        finally
        {
            // Release gate to unblock any remaining pipeline tasks
            _host?.Gate.Release(100);
            await page.CloseAsync();
        }
    }
}
