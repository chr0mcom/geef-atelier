using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Persistence.Sources;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Tests.Web.Uploads;
using Geef.Atelier.Web.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Geef.Atelier.Tests.Web.E2E;

/// <summary>
/// The upload page in a real browser on a phone screen, in the dark and the light palette: choose the file, see the receipt
/// and the line to go back, without sideways scrolling, also for a very long file name. The real page upload endpoint runs;
/// the ticket service is <see cref="FakeUploadTickets"/>.
/// </summary>
[Collection("Playwright")]
public sealed class UploadPageFlowTests(PlaywrightFixture pw, PostgresFixture pg) : IAsyncLifetime
{
    private const string User = "test-user";

    private readonly FakeUploadTickets _tickets = new();
    private WebTestHost? _host;

    public async Task InitializeAsync() =>
        _host = await WebTestHost.StartAsync(pg, initialGateCount: 100,
            configureServices: s =>
            {
                s.AddSingleton<IUploadTicketService>(_tickets);
                s.AddSingleton<IUploadTicketRepository>(_tickets);
            },
            configureApp: app => app.MapUploadEndpoints());

    public async Task DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
    }

    [Theory]
    [InlineData("noir")]
    [InlineData("vellum")]
    public async Task OnAPhoneTheFileArrivesAndNothingScrollsSideways(string palette)
    {
        var longName = "Offer-for-the-new-garden-house-with-all-the-drawings-and-the-price-list-from-the-carpenter-version-three-final.pdf";
        var id = _tickets.Add(User, longName);
        var context = await pw.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 360, Height = 740 } });
        await context.AddCookiesAsync([new Cookie { Name = "Atelier.Theme", Value = palette, Url = _host!.BaseUrl }]);
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_host.BaseUrl}/u/{id}");
            await page.Locator("[data-testid='upload-page'][data-state='open']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            Assert.Equal(UploadTicketLines.PageHeading, await page.Locator("h1").InnerTextAsync());
            await AssertFitsAsync(page, $"open-{palette}");

            await page.Locator("[data-testid='upload-drop'][data-ready='true']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            await page.Locator("[data-testid='upload-file-input']").SetInputFilesAsync(new FilePayload
            {
                Name = "garden.md",
                MimeType = "text/markdown",
                Buffer = "# Garden house\nThe carpenter needs four weeks."u8.ToArray(),
            });

            await page.Locator("[data-testid='upload-page'][data-state='done']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            Assert.Equal(UploadTicketLines.PageDone, await page.Locator("[data-testid='upload-done']").InnerTextAsync());
            Assert.Equal("garden.md", await page.Locator("[data-testid='source-card-name']").InnerTextAsync());
            Assert.False(await page.Locator("[data-testid='source-card-remove']").IsVisibleAsync());
            await AssertFitsAsync(page, $"done-{palette}");

            var received = Assert.Single(_tickets.PageUploads);
            Assert.Equal((id, User, "garden.md"), (received.Id, received.Owner, received.FileName));
            Assert.Equal(UploadClaimProbe.Used, _tickets.StateOf(id));

            await page.ReloadAsync();
            await page.Locator("[data-testid='upload-page'][data-state='used']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            await AssertFitsAsync(page, $"used-{palette}");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task AnUnusableFileOnAPhoneShowsTheReasonAndTheFieldForAnotherFile()
    {
        var id = _tickets.Add(User, "scan.pdf");
        _tickets.Outcome = (_, _) => new UploadOutcome(422,
            new SourceReceipt(null, "scan.pdf", null, 2_048, null, 0, 0, SourceQuality.Unreadable, false, false, SourceBudgetClass.FullText, string.Empty, [],
                "This PDF has no readable text. It may be a scan; scanned files are not supported yet."), null, null);
        var page = await pw.Browser.NewPageAsync(new BrowserNewPageOptions { ViewportSize = new ViewportSize { Width = 360, Height = 740 } });
        try
        {
            await page.GotoAsync($"{_host!.BaseUrl}/u/{id}");
            await page.Locator("[data-testid='upload-page'][data-state='open']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            await page.Locator("[data-testid='upload-drop'][data-ready='true']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            await page.Locator("[data-testid='upload-file-input']").SetInputFilesAsync(new FilePayload { Name = "scan.pdf", MimeType = "application/pdf", Buffer = [0x25, 0x50, 0x44, 0x46] });

            await page.Locator("[data-testid='upload-page'][data-state='problem']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            Assert.Equal("This PDF has no readable text. It may be a scan; scanned files are not supported yet. Choose a different file.",
                await page.Locator("[data-testid='upload-problem'] .upload-state-line").InnerTextAsync());
            Assert.True(await page.Locator("[data-testid='upload-drop']").IsVisibleAsync());
            await AssertFitsAsync(page, "problem");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    private static async Task AssertFitsAsync(IPage page, string name)
    {
        var overflow = await page.EvaluateAsync<int>("document.documentElement.scrollWidth - document.documentElement.clientWidth");
        var wide = await page.EvaluateAsync<string>("""
            () => {
                const width = document.documentElement.clientWidth;
                return [...document.querySelectorAll('body *')]
                    .filter(e => e.getBoundingClientRect().right > width + 1)
                    .slice(0, 5)
                    .map(e => e.tagName + '.' + e.className)
                    .join(', ');
            }
            """);
        if (Environment.GetEnvironmentVariable("ATELIER_SCREENSHOT_DIR") is { Length: > 0 } dir)
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(dir, $"upload-{name}.png"), FullPage = true });
        Assert.True(overflow <= 0, $"The upload page ({name}) scrolls sideways by {overflow} px on a phone: {wide}");
    }
}
