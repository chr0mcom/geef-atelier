using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Tests.Web.Components.Orders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Geef.Atelier.Tests.Web.E2E;

/// <summary>
/// The finished order page in a real browser: "What was checked", "Open points", "Assumptions" and "From your files" are
/// visible below the text in that order, and the page fits a phone screen without sideways scrolling. The order service is an
/// in-memory fake with a result that has every field of stage 3 filled.
/// </summary>
[Collection("Playwright")]
public sealed class ResultHonestyFlowTests(PlaywrightFixture pw, PostgresFixture pg) : IAsyncLifetime
{
    private const string User = "test-user";

    private static readonly string[] Blocks = ["order-result", "order-checks", "order-open-points", "order-assumptions", "order-sources-used"];

    private readonly FakeOrderService _orders = new();
    private WebTestHost? _host;

    public async Task InitializeAsync() =>
        _host = await WebTestHost.StartAsync(pg, initialGateCount: 100,
            configureServices: s => s.AddSingleton<IOrderService>(_orders));

    public async Task DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
    }

    private Guid AddFinishedOrder()
    {
        var view = _orders.Add(User, OrderTestData.View(OrderDisplayState.DeliveredWithOpenPoints) with
        {
            Clarification = OrderTestData.Clarification(outcome: IntakeOutcome.Answered, language: "de"),
        });
        _orders.SetResult(HonestResultData.Full(view) with { Checks = HonestResultData.Checks(openPoints: true, changed: true) });
        return view.OrderId;
    }

    [Fact]
    public async Task AllBlocksAreVisibleBelowTheTextInOrder()
    {
        var orderId = AddFinishedOrder();
        var page = await pw.Browser.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_host!.BaseUrl}/orders/{orderId}");
            await page.Locator("[data-testid='order-checks']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

            foreach (var id in Blocks)
                await page.Locator($"[data-testid='{id}']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
            var tops = await page.EvaluateAsync<double[]>(
                "ids => ids.map(id => document.querySelector(`[data-testid='${id}']`).getBoundingClientRect().top)", Blocks);
            Assert.Equal(Blocks.Length, tops.Length);
            Assert.Equal(tops.OrderBy(t => t), tops);
            Assert.Equal("Please look at the open points below.", (await page.Locator("[data-testid='order-checks-review']").TextContentAsync())?.Trim());
            Assert.Equal(OrderCheckLines.Limits, (await page.Locator("[data-testid='order-checks-limits']").TextContentAsync())?.Trim());
            Assert.Equal("Shows the file was read and quoted — not that every statement is backed.",
                (await page.Locator("[data-testid='order-sources-caveat']").TextContentAsync())?.Trim());
            Assert.Null(await page.Locator("[data-testid='order-how-made']").GetAttributeAsync("open"));
            if (Environment.GetEnvironmentVariable("ATELIER_SCREENSHOT_DIR") is { Length: > 0 } dir)
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(dir, "result-desktop.png"), FullPage = true });
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task TheResultFitsAPhoneScreenWithoutSidewaysScrolling()
    {
        var orderId = AddFinishedOrder();
        var page = await pw.Browser.NewPageAsync(new BrowserNewPageOptions { ViewportSize = new ViewportSize { Width = 360, Height = 740 } });
        try
        {
            await page.GotoAsync($"{_host!.BaseUrl}/orders/{orderId}");
            await page.Locator("[data-testid='order-sources-used']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

            var overflow = await page.EvaluateAsync<int>("document.documentElement.scrollWidth - document.documentElement.clientWidth");
            var wide = await page.EvaluateAsync<string>(WideElementsScript);
            Assert.True(overflow <= 0, $"The result page scrolls sideways by {overflow} px on a phone: {wide}");
            if (Environment.GetEnvironmentVariable("ATELIER_SCREENSHOT_DIR") is { Length: > 0 } dir)
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(dir, "result-phone.png"), FullPage = true });
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    private const string WideElementsScript = """
        () => {
            const width = document.documentElement.clientWidth;
            return [...document.querySelectorAll('body *')]
                .filter(e => e.getBoundingClientRect().right > width + 1)
                .slice(0, 8)
                .map(e => e.tagName.toLowerCase() + (e.className && typeof e.className === 'string' ? '.' + e.className.trim().split(/\s+/).join('.') : '') + ' ' + Math.round(e.getBoundingClientRect().right))
                .join(', ');
        }
        """;
}
