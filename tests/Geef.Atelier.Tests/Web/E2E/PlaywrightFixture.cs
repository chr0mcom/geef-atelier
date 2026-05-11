using Microsoft.Playwright;

namespace Geef.Atelier.Tests.Web.E2E;

/// <summary>
/// Creates and manages a single <see cref="IPlaywright"/> instance and a shared Chromium browser
/// for the entire Playwright test collection.
/// </summary>
public sealed class PlaywrightFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private IBrowser?    _browser;

    /// <summary>The shared Playwright instance.</summary>
    public IPlaywright Playwright => _playwright ?? throw new InvalidOperationException("Not initialized.");

    /// <summary>The shared Chromium browser (headless).</summary>
    public IBrowser Browser => _browser ?? throw new InvalidOperationException("Not initialized.");

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        _browser    = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args     = ["--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage"]
        });
    }

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        if (_browser is not null)
            await _browser.DisposeAsync();
        _playwright?.Dispose();
    }
}
