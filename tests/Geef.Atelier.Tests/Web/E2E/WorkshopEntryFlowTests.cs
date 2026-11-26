using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Tests.Web.Components.Orders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Geef.Atelier.Tests.Web.E2E;

/// <summary>
/// Stage 8a (D-077) in a real browser on a phone, in a dark and a light theme: a person who is not an administrator sees only
/// "New" and "My work", no link to the older form and no technical details, and nothing scrolls sideways. The workshop pages
/// still open when their address is typed. With the switch off the menu is back and fits as well.
/// </summary>
[Collection("Playwright")]
public sealed class WorkshopEntryFlowTests(PlaywrightFixture pw, PostgresFixture pg) : IAsyncLifetime
{
    private readonly FakeOrderService _orders = new();
    private WebTestHost? _host;

    public async Task InitializeAsync() =>
        _host = await WebTestHost.StartAsync(pg, initialGateCount: 100,
            configureServices: s => s.AddSingleton<IOrderService>(_orders));

    public async Task DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
    }

    [Theory]
    [InlineData("noir")]
    [InlineData("vellum")]
    public async Task OnAPhoneANormalUserSeesNoWorkshopEntriesAndNothingScrollsSideways(string palette)
    {
        var view = _orders.Add("test-user", OrderTestData.View(OrderDisplayState.Delivered));
        _orders.SetResult(OrderTestData.Result(view, text: "Dear customer,\n\nyour delivery will arrive two weeks later."));
        var context = await PhoneAsync(_host!, palette);
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_host!.BaseUrl}/start");
            await page.Locator("[data-testid='start-page']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            await WaitInteractiveAsync(page);
            Assert.Equal(new[] { "/start", "/work", "/how-it-works" }, await NavHrefsAsync(page));
            Assert.Equal(0, await page.Locator("[data-testid='nav-workshop']").CountAsync());
            Assert.Equal(0, await page.Locator("[data-testid='start-more-options']").CountAsync());
            Assert.True(await page.Locator("[data-testid='nav-my-work']").IsVisibleAsync());
            await AssertFitsAsync(page, $"start-{palette}");

            await page.GotoAsync($"{_host.BaseUrl}/orders/{view.OrderId}");
            await page.Locator("[data-testid='order-result']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            await page.Locator("[data-testid='order-how-made'] summary").ClickAsync();
            await page.Locator("[data-testid='order-how-request']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            Assert.Equal(0, await page.Locator("[data-testid='order-technical-details']").CountAsync());
            Assert.Equal(0, await page.Locator("[data-testid='nav-workshop']").CountAsync());
            await AssertFitsAsync(page, $"order-{palette}");

            await page.GotoAsync($"{_host.BaseUrl}/account/connected-clients");
            await page.Locator("[data-testid='connected-clients-page']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            Assert.Equal("/work", await page.Locator("[data-testid='btn-back']").GetAttributeAsync("href"));
            await AssertFitsAsync(page, $"connected-clients-{palette}");

            var response = await page.GotoAsync($"{_host.BaseUrl}/runs");
            Assert.Equal(200, response!.Status);
            await page.WaitForSelectorAsync("main h1", new PageWaitForSelectorOptions { Timeout = 15_000 });
            Assert.Equal(new[] { "/start", "/work", "/how-it-works" }, await NavHrefsAsync(page));
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Theory]
    [InlineData("noir")]
    [InlineData("vellum")]
    public async Task WithTheSwitchOffTheMenuIsBackAndFitsOnAPhone(string palette)
    {
        await using var host = await WebTestHost.StartAsync(pg, initialGateCount: 100,
            configureServices: s => s.Configure<UiOptions>(o => o.WorkshopForAdminsOnly = false));
        var context = await PhoneAsync(host, palette);
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{host.BaseUrl}/start");
            await page.Locator("[data-testid='start-page']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            await WaitInteractiveAsync(page);
            Assert.Equal("/new", await page.Locator("[data-testid='start-more-options']").GetAttributeAsync("href"));
            await AssertFitsAsync(page, $"switch-off-{palette}");

            await page.Locator("[data-testid='nav-workshop']").ClickAsync();
            await page.Locator(".nav-dropdown a[href='/runs']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            Assert.True(await page.Locator(".nav-dropdown a[href='/new']").IsVisibleAsync());
            await AssertFitsAsync(page, $"switch-off-open-{palette}");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    private async Task<IBrowserContext> PhoneAsync(WebTestHost host, string palette)
    {
        var context = await pw.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 360, Height = 740 } });
        await context.AddCookiesAsync([new Cookie { Name = "Atelier.Theme", Value = palette, Url = host.BaseUrl }]);
        return context;
    }

    private static async Task WaitInteractiveAsync(IPage page) =>
        await page.Locator("[data-testid='start-blocked-reason']").Filter(new LocatorFilterOptions { HasNotText = "Loading" })
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

    private static async Task<string[]> NavHrefsAsync(IPage page) =>
        (await page.Locator(".nav-links a.nav-link").EvaluateAllAsync<string[]>("els => els.map(e => e.getAttribute('href'))")).ToArray();

    private static async Task AssertFitsAsync(IPage page, string name)
    {
        var overflow = await page.EvaluateAsync<int>("document.documentElement.scrollWidth - document.documentElement.clientWidth");
        if (Environment.GetEnvironmentVariable("ATELIER_SCREENSHOT_DIR") is { Length: > 0 } dir)
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(dir, $"workshop-{name}.png"), FullPage = true });
        Assert.True(overflow <= 0, $"The page ({name}) scrolls sideways by {overflow} px on a phone.");
    }
}
