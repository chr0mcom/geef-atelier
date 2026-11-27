using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Tests.Web.Components.Orders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Geef.Atelier.Tests.Web.E2E;

/// <summary>
/// The dark palettes in a real browser: every text of a result with a table, a quote, a link and code on its light sheet, of the user
/// chip in the navigation, of the filters of "My work" and of the run list keeps a contrast of at least 4.5:1.
/// </summary>
[Collection("Playwright")]
public sealed class DarkPaletteContrastTests(PlaywrightFixture pw, PostgresFixture pg) : IAsyncLifetime
{
    private const string Text =
        "# Invitation\n\nDear all, *please* come on **5 May** to [our page](https://example.org/page) and bring `your card`.\n\n" +
        "| Item | Time |\n|---|---|\n| Welcome | 19:00 |\n\n> Please be on time.\n\n```\na code block\n```\n\n---\n\nKind regards";

    private readonly FakeOrderService _orders = new();
    private WebTestHost? _host;

    public async Task InitializeAsync() =>
        _host = await WebTestHost.StartAsync(pg, initialGateCount: 100, configureServices: s => s.AddSingleton<IOrderService>(_orders));

    public async Task DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
    }

    [Theory]
    [InlineData("noir")]
    [InlineData("petrol")]
    public async Task Result_navigation_and_filters_are_readable(string palette)
    {
        var view = _orders.Add("test-user", OrderTestData.View(OrderDisplayState.Delivered));
        _orders.SetResult(OrderTestData.Result(view, text: Text));
        var context = await pw.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1366, Height = 900 } });
        await context.AddCookiesAsync([new Cookie { Name = "Atelier.Theme", Value = palette, Url = _host!.BaseUrl }]);
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_host.BaseUrl}/orders/{view.OrderId}");
            await page.Locator("[data-testid='order-page'][data-interactive='true'] [data-testid='order-result'] blockquote")
                .WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
            await page.AssertContrastAsync($"order-{palette}", "body");

            foreach (var path in new[] { "/work", "/runs" })
            {
                await page.GotoAsync($"{_host.BaseUrl}{path}");
                await page.Locator(".filter-pill").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
                await page.AssertContrastAsync($"{path}-{palette}", "body");
            }
        }
        finally
        {
            await context.CloseAsync();
        }
    }
}
