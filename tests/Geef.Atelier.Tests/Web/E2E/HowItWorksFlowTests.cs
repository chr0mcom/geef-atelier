using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Tests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Geef.Atelier.Tests.Web.E2E;

/// <summary>
/// The page "How it works" in a real browser: open without signing in from the landing page, readable on a desktop and on a phone in
/// a light and a dark theme without sideways scrolling or layout shift, reachable with the keyboard, and for a signed-in person with
/// an exception the feature appears while the values for everyone stay off.
/// </summary>
[Collection("Playwright")]
public sealed class HowItWorksFlowTests(PlaywrightFixture pw, PostgresFixture pg)
{
    public static TheoryData<string, int, int> Views => new()
    {
        { "vellum", 1280, 900 },
        { "noir", 1280, 900 },
        { "vellum", 360, 740 },
        { "noir", 360, 740 },
    };

    [Theory]
    [MemberData(nameof(Views))]
    public async Task Signed_out_the_page_opens_from_the_landing_page_fits_and_shows_the_values_for_everyone(string palette, int width, int height)
    {
        await using var host = await WebTestHost.StartAsync(pg, authenticated: false, configureServices: Shipped);
        var context = await ContextAsync(host, palette, width, height);
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{host.BaseUrl}/");
            var link = page.Locator(width < 700 ? "[data-testid='landing-hero-how']" : "[data-testid='landing-how-it-works']");
            await link.ClickAsync();
            await page.Locator("[data-testid='how-it-works']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            Assert.EndsWith("/how-it-works", page.Url, StringComparison.Ordinal);

            Assert.Equal("false", await page.Locator("[data-testid='hiw-status-change']").GetAttributeAsync("data-on"));
            Assert.Equal("false", await page.Locator("[data-testid='hiw-status-research']").GetAttributeAsync("data-on"));
            Assert.Equal(0, await page.Locator("[data-testid='hiw-step-change']").CountAsync());
            Assert.True(await page.Locator("[data-testid='hiw-sign-in']").IsVisibleAsync());

            await page.GotoAsync($"{host.BaseUrl}/how-it-works");
            await page.Locator("[data-testid='how-it-works']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            await AssertNoLayoutShiftAsync(page);
            await AssertFitsAsync(page, $"signed-out-{palette}-{width}");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Theory]
    [MemberData(nameof(Views))]
    public async Task A_signed_in_person_with_an_exception_sees_changing_and_the_menu_leads_there(string palette, int width, int height)
    {
        await using var host = await WebTestHost.StartAsync(pg, configureServices: s =>
        {
            Shipped(s);
            s.Configure<OrderOptions>(o => o.ReviseOverrides["test-user"] = ReviseMode.On);
        });
        var context = await ContextAsync(host, palette, width, height);
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{host.BaseUrl}/work");
            await page.Locator("[data-testid='nav-how-it-works']").ClickAsync();
            await page.Locator("[data-testid='how-it-works']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

            Assert.Equal("true", await page.Locator("[data-testid='hiw-status-change']").GetAttributeAsync("data-on"));
            Assert.True(await page.Locator("[data-testid='hiw-step-change']").IsVisibleAsync());
            Assert.Equal("Off", (await page.Locator("[data-testid='hiw-admins'] tbody tr", new PageLocatorOptions { HasText = "Orders:Revise" })
                .Locator("td").Nth(2).TextContentAsync())?.Trim());
            Assert.True(await page.Locator("[data-testid='hiw-start']").IsVisibleAsync());

            await AssertFitsAsync(page, $"signed-in-{palette}-{width}");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task The_ways_in_and_the_links_can_be_reached_with_the_keyboard()
    {
        await using var host = await WebTestHost.StartAsync(pg, authenticated: false, configureServices: Shipped);
        var context = await ContextAsync(host, "vellum", 1280, 900);
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{host.BaseUrl}/how-it-works");
            await page.Locator("[data-testid='how-it-works']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

            var reached = false;
            for (var i = 0; i < 25 && !reached; i++)
            {
                await page.Keyboard.PressAsync("Tab");
                reached = await page.EvaluateAsync<string?>("document.activeElement?.getAttribute('data-testid')") == "hiw-way-assistant";
            }
            Assert.True(reached, "The way in for AI assistants could not be reached with Tab.");
            var outline = await page.EvaluateAsync<string>("getComputedStyle(document.activeElement).outlineStyle");
            Assert.NotEqual("none", outline);

            await page.Keyboard.PressAsync("Enter");
            await page.WaitForFunctionAsync("location.hash === '#assistant'", null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    private static void Shipped(IServiceCollection services)
    {
        services.Configure<OrderOptions>(o => o.IntakeMode = IntakeMode.On);
        services.Configure<UploadOptions>(o => o.Tickets = UploadTicketsMode.On);
        services.Configure<DirectOptions>(o => o.FactCheckMode = Geef.Atelier.Core.Domain.FactCheck.FactCheckMode.Report);
    }

    private async Task<IBrowserContext> ContextAsync(WebTestHost host, string palette, int width, int height)
    {
        var context = await pw.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = width, Height = height } });
        await context.AddCookiesAsync([new Cookie { Name = "Atelier.Theme", Value = palette, Url = host.BaseUrl }]);
        return context;
    }

    private static async Task AssertNoLayoutShiftAsync(IPage page)
    {
        var shift = await page.EvaluateAsync<string>(
            """
            () => new Promise(resolve => {
                let total = 0; const nodes = [];
                new PerformanceObserver(list => { for (const e of list.getEntries()) if (!e.hadRecentInput) { total += e.value;
                    for (const s of e.sources || []) nodes.push((s.node && (s.node.className || s.node.nodeName)) + ':' + e.value.toFixed(3) + '@' + Math.round(e.startTime)); } })
                    .observe({ type: 'layout-shift', buffered: true });
                setTimeout(() => resolve(total.toFixed(3) + ' ' + nodes.join(' | ')), 1500);
            })
            """);
        Assert.True(double.Parse(shift.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture) < 0.05, $"The page shifted its layout by {shift}.");
    }

    private static async Task AssertFitsAsync(IPage page, string name)
    {
        var overflow = await page.EvaluateAsync<int>("document.documentElement.scrollWidth - document.documentElement.clientWidth");
        if (Environment.GetEnvironmentVariable("ATELIER_SCREENSHOT_DIR") is { Length: > 0 } dir)
        {
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(dir, $"how-it-works-{name}.png"), FullPage = true });
            foreach (var part in new[] { "steps", "example", "status" })
            {
                await page.Locator($"#{part}").ScrollIntoViewIfNeededAsync();
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(dir, $"how-it-works-{name}-{part}.png") });
            }
        }
        Assert.True(overflow <= 0, $"The page ({name}) scrolls sideways by {overflow} px.");
    }
}
