using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Tests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Geef.Atelier.Tests.Web.E2E;

/// <summary>
/// The technical run page follows a run live. The gate of the fake model starts closed, so the run cannot end before the page is
/// open; a marker set in the page then shows that the finished status arrived without a reload.
/// </summary>
[Collection("Playwright")]
public sealed class LiveUpdateFlowTests(PlaywrightFixture pw, PostgresFixture pg) : IAsyncLifetime
{
    private WebTestHost? _host;

    public async Task InitializeAsync() =>
        _host = await WebTestHost.StartAsync(pg, initialGateCount: 0);

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
            var runId = await _host!.CreateRunAsync("Live update test briefing — pipeline should complete.");
            await page.GotoAsync($"{_host.BaseUrl}/runs/{runId}", new PageGotoOptions { Timeout = 90_000 });
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 90_000 });
            Assert.Equal(0, await page.Locator("[data-status='Completed']").CountAsync());

            await page.EvaluateAsync("() => { window.__noReloadMarker = true; }");

            _host.Gate.Release(100);
            var status = await WaitForEndAsync(runId);
            Assert.Equal(RunStatus.Completed, status);

            await page.WaitForSelectorAsync("[data-status='Completed']",
                new PageWaitForSelectorOptions { Timeout = 60_000 });

            var markerPresent = await page.EvaluateAsync<bool>("() => window.__noReloadMarker === true");
            Assert.True(markerPresent, "Expected a live update without page reload, but the page was reloaded.");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    /// <summary>Waits on the server until the run has ended, so that a run that fails says so instead of timing out in the browser.</summary>
    private async Task<RunStatus> WaitForEndAsync(Guid runId)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(120);
        while (true)
        {
            await using var scope = _host!.Services.CreateAsyncScope();
            var run = await scope.ServiceProvider.GetRequiredService<IRunRepository>().GetByIdAsync(runId, CancellationToken.None);
            if (run!.Status is RunStatus.Completed or RunStatus.Failed or RunStatus.Aborted || DateTimeOffset.UtcNow > deadline)
                return run.Status;
            await Task.Delay(200);
        }
    }
}
