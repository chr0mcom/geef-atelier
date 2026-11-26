using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Web.Components.Pages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Geef.Atelier.Tests.Web.E2E;

/// <summary>
/// The start page in a real browser: readable on a desktop and on a phone in a light and a dark theme without sideways scrolling,
/// layout shift or text below the contrast minimum, the way to start is visible at once and reachable with the keyboard, the steps
/// play once and end with every step marked, and a signed-in person is led to a new text instead of the sign-in page.
/// </summary>
[Collection("Playwright")]
public sealed class LandingFlowTests(PlaywrightFixture pw, PostgresFixture pg)
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
    public async Task Signed_out_the_page_fits_keeps_its_contrast_and_shows_the_way_to_start(string palette, int width, int height)
    {
        await using var host = await WebTestHost.StartAsync(pg, authenticated: false, configureServices: Shipped);
        var context = await ContextAsync(host, palette, width, height, ReducedMotion.Reduce);
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{host.BaseUrl}/");
            await page.Locator("[data-testid='landing']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

            var start = page.Locator("[data-testid='landing-hero-start']");
            Assert.True(await start.IsVisibleAsync());
            Assert.Equal("/login", await start.GetAttributeAsync("href"));
            var box = await start.BoundingBoxAsync();
            Assert.NotNull(box);
            Assert.True(box!.Y + box.Height <= height, $"The way to start is below the first screen ({box.Y + box.Height} px of {height} px).");
            Assert.Equal(Landing.Title, await page.TitleAsync());
            Assert.Equal(Landing.Description, await page.Locator("meta[name='description']").GetAttributeAsync("content"));

            await AssertNoLayoutShiftAsync(page);
            await AssertContrastAsync(page, $"{palette}-{width}");
            await AssertFitsAsync(page, $"signed-out-{palette}-{width}");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task The_steps_play_once_and_end_with_every_step_marked()
    {
        await using var host = await WebTestHost.StartAsync(pg, authenticated: false, configureServices: Shipped);
        var context = await ContextAsync(host, "vellum", 1280, 900, ReducedMotion.NoPreference);
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{host.BaseUrl}/");
            var flow = page.Locator("[data-testid='landing-flow']");
            await flow.ScrollIntoViewIfNeededAsync();
            await page.WaitForFunctionAsync(
                "() => { const s = [...document.querySelectorAll('.lp-flow-step')]; return s.length > 0 && s.every(x => x.classList.contains('has-passed')); }",
                null, new PageWaitForFunctionOptions { Timeout = 15_000 });
            Assert.Equal(5, await page.Locator(".lp-flow-step").CountAsync());
            Assert.Equal(0, await page.Locator(".lp-flow-step.is-active").CountAsync());
            Assert.Contains("run", await flow.GetAttributeAsync("class") ?? "", StringComparison.Ordinal);
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task The_way_to_start_and_the_ways_in_can_be_reached_with_the_keyboard()
    {
        await using var host = await WebTestHost.StartAsync(pg, authenticated: false, configureServices: Shipped);
        var context = await ContextAsync(host, "noir", 1280, 900, ReducedMotion.Reduce);
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{host.BaseUrl}/");
            await page.Locator("[data-testid='landing']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

            await TabToAsync(page, "landing-hero-start", 20);
            Assert.NotEqual("none", await page.EvaluateAsync<string>("getComputedStyle(document.activeElement).outlineStyle"));

            await page.Locator("[data-testid='landing-for-whom-assistant'] a").FocusAsync();
            Assert.NotEqual("none", await page.EvaluateAsync<string>("getComputedStyle(document.activeElement).outlineStyle"));
            await page.Keyboard.PressAsync("Enter");
            await page.Locator("[data-testid='how-it-works']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            Assert.EndsWith("/how-it-works#assistant", page.Url, StringComparison.Ordinal);
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Theory]
    [InlineData(1280, 900)]
    [InlineData(360, 740)]
    public async Task A_signed_in_person_is_led_to_a_new_text(int width, int height)
    {
        await using var host = await WebTestHost.StartAsync(pg, configureServices: Shipped);
        var context = await ContextAsync(host, "vellum", width, height, ReducedMotion.Reduce);
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{host.BaseUrl}/");
            await page.Locator("[data-testid='landing']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

            Assert.Equal("/start", await page.Locator("[data-testid='landing-hero-start']").GetAttributeAsync("href"));
            Assert.True(await page.Locator("[data-testid='landing-nav-start']").IsVisibleAsync());
            Assert.Equal(0, await page.Locator("[data-testid='landing-hero-account']").CountAsync());
            await AssertFitsAsync(page, $"signed-in-vellum-{width}");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    private static async Task TabToAsync(IPage page, string testId, int maxTabs)
    {
        for (var i = 0; i < maxTabs; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            if (await page.EvaluateAsync<string?>("document.activeElement?.getAttribute('data-testid')") == testId)
                return;
        }
        Assert.Fail($"{testId} could not be reached with {maxTabs} presses of Tab.");
    }

    private static void Shipped(IServiceCollection services)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Geef.Atelier.slnx")))
            dir = dir.Parent;
        var shipped = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(dir!.FullName, "src", "Geef.Atelier.Web", "appsettings.json"), optional: false, reloadOnChange: false)
            .Build();
        services.Configure<OrderOptions>(shipped.GetSection(OrderOptions.SectionName));
        services.Configure<IntakeOptions>(shipped.GetSection(IntakeOptions.SectionName));
        services.Configure<ResearchOptions>(shipped.GetSection(ResearchOptions.SectionName));
        services.Configure<DirectOptions>(shipped.GetSection(DirectOptions.SectionName));
        services.Configure<UploadOptions>(shipped.GetSection(UploadOptions.SectionName));
        services.Configure<UiOptions>(shipped.GetSection(UiOptions.SectionName));
        services.Configure<LearningOptions>(shipped.GetSection(LearningOptions.SectionName));
    }

    private async Task<IBrowserContext> ContextAsync(WebTestHost host, string palette, int width, int height, ReducedMotion motion)
    {
        var context = await pw.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            ReducedMotion = motion,
        });
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

    private static async Task AssertContrastAsync(IPage page, string name)
    {
        var failures = await page.EvaluateAsync<string[]>(
            """
            () => {
                const canvas = document.createElement('canvas'); canvas.width = canvas.height = 1;
                const ctx = canvas.getContext('2d', { willReadFrequently: true });
                const rgba = c => { ctx.clearRect(0, 0, 1, 1); ctx.fillStyle = 'rgba(0,0,0,0)'; ctx.fillStyle = c; ctx.fillRect(0, 0, 1, 1);
                    const d = ctx.getImageData(0, 0, 1, 1).data; return [d[0], d[1], d[2], d[3] / 255]; };
                const over = (top, under) => { const a = top[3]; return [0, 1, 2].map(i => top[i] * a + under[i] * (1 - a)).concat(1); };
                const background = el => {
                    const layers = [];
                    for (let n = el; n; n = n.parentElement) {
                        const c = rgba(getComputedStyle(n).backgroundColor);
                        if (c[3] > 0) { layers.push(c); if (c[3] >= 1) break; }
                    }
                    let result = [255, 255, 255, 1];
                    for (let i = layers.length - 1; i >= 0; i--) result = over(layers[i], result);
                    return result;
                };
                const lum = c => { const v = c.slice(0, 3).map(x => { x /= 255; return x <= 0.03928 ? x / 12.92 : Math.pow((x + 0.055) / 1.055, 2.4); });
                    return 0.2126 * v[0] + 0.7152 * v[1] + 0.0722 * v[2]; };
                const failures = [];
                const scope = document.querySelector('[data-testid="landing"]');
                const walker = document.createTreeWalker(scope, NodeFilter.SHOW_TEXT);
                const seen = new Set();
                for (let t = walker.nextNode(); t; t = walker.nextNode()) {
                    const el = t.parentElement;
                    if (!t.textContent.trim() || seen.has(el)) continue;
                    seen.add(el);
                    if (el.closest('#film, svg, [aria-hidden="true"], .visually-hidden')) continue;
                    const r = el.getBoundingClientRect();
                    if (r.width === 0 || r.height === 0) continue;
                    const style = getComputedStyle(el);
                    const bg = background(el);
                    let opacity = 1;
                    for (let n = el; n; n = n.parentElement) opacity *= parseFloat(getComputedStyle(n).opacity);
                    const ink = rgba(style.color); ink[3] *= opacity;
                    const fg = over(ink, bg);
                    const l1 = lum(fg), l2 = lum(bg);
                    const ratio = (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
                    const size = parseFloat(style.fontSize), bold = parseInt(style.fontWeight, 10) >= 700;
                    const need = size >= 24 || (bold && size >= 18.66) ? 3 : 4.5;
                    if (ratio < need) failures.push(`${ratio.toFixed(2)} < ${need}: "${t.textContent.trim().slice(0, 40)}" (${el.tagName.toLowerCase()}.${el.className})`);
                }
                return failures;
            }
            """);
        Assert.True(failures.Length == 0, $"Text below the contrast minimum ({name}):\n" + string.Join("\n", failures));
    }

    private static async Task AssertFitsAsync(IPage page, string name)
    {
        var overflow = await page.EvaluateAsync<int>("document.documentElement.scrollWidth - document.documentElement.clientWidth");
        if (Environment.GetEnvironmentVariable("ATELIER_SCREENSHOT_DIR") is { Length: > 0 } dir)
        {
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(dir, $"landing-{name}.png"), FullPage = true });
            foreach (var part in new[] { "example", "steps", "rely", "for-whom", "teams" })
            {
                await page.Locator($"#{part}").ScrollIntoViewIfNeededAsync();
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(dir, $"landing-{name}-{part}.png") });
            }
        }
        Assert.True(overflow <= 0, $"The page ({name}) scrolls sideways by {overflow} px.");
    }
}
