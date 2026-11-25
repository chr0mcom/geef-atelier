using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Profiles;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Tests.Web.Components.Orders;
using Geef.Atelier.Tests.Web.Components.Profile;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Geef.Atelier.Tests.Web.E2E;

/// <summary>
/// The questions before writing in a real browser: the card gets the focus, answers are clicked or chosen with the arrow
/// keys, "Start writing" starts the order, "Skip and start now" ends the wait, and the card fits a phone screen. The
/// order and profile services are in-memory fakes; the real intake arrives with Q1/Q2.
/// </summary>
[Collection("Playwright")]
public sealed class ClarifyFlowTests(PlaywrightFixture pw, PostgresFixture pg) : IAsyncLifetime
{
    private const string User = "test-user";

    private readonly FakeOrderService _orders = new();
    private readonly FakeProfileService _profiles = new();
    private WebTestHost? _host;

    public async Task InitializeAsync() =>
        _host = await WebTestHost.StartAsync(pg, initialGateCount: 100,
            configureServices: s =>
            {
                s.AddSingleton<IOrderService>(_orders);
                s.AddSingleton<IProfileService>(_profiles);
            });

    public async Task DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
    }

    [Fact]
    public async Task TheCardTakesTheFocusAndStartWritingSendsTheChosenAnswers()
    {
        var order = _orders.Add(User, OrderTestData.Asking(OrderTestData.Clarification(
            questions: [OrderTestData.LengthQuestion(), OrderTestData.Question(1)])));
        var page = await pw.Browser.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_host!.BaseUrl}/orders/{order.OrderId}");
            await page.Locator("[data-testid='clarify-card']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            await WaitForAsync(page, "document.activeElement && document.activeElement.id === 'clarify-card-title'");

            await page.ClickAsync("[data-testid='question-block'][data-id='length'] [data-id='short']");
            await page.Locator("[data-testid='question-block'][data-id='length'] [data-id='short'][aria-checked='true']").WaitForAsync();
            await page.Keyboard.PressAsync("ArrowRight");
            await page.Locator("[data-testid='question-block'][data-id='length'] [data-id='medium'][aria-checked='true']").WaitForAsync();
            await WaitForAsync(page, "document.activeElement && document.activeElement.dataset.id === 'medium'");

            await page.ClickAsync("[data-testid='question-block'][data-id='q1'] [data-testid='question-free-toggle']");
            await page.FillAsync("[data-testid='question-block'][data-id='q1'] [data-testid='question-free-text']", "Am 12. Juli");
            await page.ClickAsync("[data-testid='clarify-start']");

            await page.Locator("[data-testid='order-state'][data-state='queued']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            await page.Locator("[data-testid='clarify-summary']:not(.clarify-summary--inline)").WaitForAsync();
            var answers = Assert.Single(_orders.Answered).Answers;
            Assert.Equal(new OrderAnswer("length", "medium", null, false, AnswerOrigin.User), answers[0]);
            Assert.Equal(new OrderAnswer("q1", null, "Am 12. Juli", false, AnswerOrigin.User), answers[1]);
            Assert.Equal(0, await page.Locator("[data-testid='clarify-card']").CountAsync());
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    /// <summary>
    /// The brand in the top bar looks like a name, not like a link (no underline, no link blue), and the heading of the card
    /// takes the focus for screen readers without showing a frame around it.
    /// </summary>
    [Fact]
    public async Task TheBrandIsNoBlueLinkAndTheFocusedCardTitleHasNoFrame()
    {
        var order = _orders.Add(User, OrderTestData.Asking(OrderTestData.Clarification(questions: [OrderTestData.LengthQuestion()])));
        var page = await pw.Browser.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_host!.BaseUrl}/orders/{order.OrderId}");
            await page.Locator("[data-testid='clarify-card']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            await WaitForAsync(page, "document.activeElement && document.activeElement.id === 'clarify-card-title'");

            var brand = await page.EvaluateAsync<string[]>("""
                () => {
                    const brand = document.querySelector('.nav-brand');
                    const style = getComputedStyle(brand);
                    return [style.textDecorationLine, style.color, getComputedStyle(brand.parentElement).color];
                }
                """);
            Assert.Equal("none", brand[0]);
            Assert.NotEqual("rgb(0, 0, 238)", brand[1]);
            Assert.Equal(brand[2], brand[1]);

            var focus = await page.EvaluateAsync<string[]>("""
                () => {
                    const heading = document.activeElement;
                    return [heading.id, heading.getAttribute('tabindex'), getComputedStyle(heading).outlineStyle, String(heading.matches(':focus'))];
                }
                """);
            Assert.Equal("clarify-card-title", focus[0]);
            Assert.Equal("-1", focus[1]);
            Assert.Equal("none", focus[2]);
            Assert.Equal("true", focus[3]);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task SkipAndStartNowEndsTheWait()
    {
        var order = _orders.Add(User, OrderTestData.Analyzing());
        var page = await pw.Browser.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_host!.BaseUrl}/orders/{order.OrderId}");
            await page.Locator("[data-testid='intake-waiting']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

            await ClickUntilAsync(page, "[data-testid='intake-skip']", () => !_orders.Skipped.IsEmpty);

            await page.Locator("[data-testid='order-state'][data-state='queued']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            Assert.Equal(order.OrderId, Assert.Single(_orders.Skipped).OrderId);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    /// <summary>The order page with the card and the top navigation fits a phone screen without sideways scrolling.</summary>
    [Fact]
    public async Task TheCardFitsAPhoneScreenWithoutSidewaysScrolling()
    {
        var order = _orders.Add(User, OrderTestData.Asking(OrderTestData.Clarification(
            questions:
            [
                OrderTestData.LengthQuestion(),
                OrderTestData.Question(1, GapTopic.Date, "Am Freitagnachmittag nach der Arbeit", "Am Samstagvormittag im Innenhof", "Am Sonntag"),
                OrderTestData.Question(2, GapTopic.Audience),
            ],
            revisionFileName: "einladung-sommerfest-entwurf-final-v3.docx",
            autoStartAt: OrderTestData.Start.AddDays(1))));
        var page = await pw.Browser.NewPageAsync(new BrowserNewPageOptions { ViewportSize = new ViewportSize { Width = 360, Height = 740 } });
        try
        {
            await page.GotoAsync($"{_host!.BaseUrl}/orders/{order.OrderId}");
            await page.Locator("[data-testid='clarify-card']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            await page.ClickAsync("[data-testid='clarify-assumption-change'] >> nth=0");
            await page.ClickAsync("[data-testid='question-free-toggle'] >> nth=0");

            var overflow = await page.EvaluateAsync<int>("document.documentElement.scrollWidth - document.documentElement.clientWidth");
            var wide = await page.EvaluateAsync<string>(WideElementsScript, "body");
            Assert.True(overflow <= 0, $"The order page scrolls sideways by {overflow} px on a phone: {wide}");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task AboutMeSavesTheProfile()
    {
        var page = await pw.Browser.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_host!.BaseUrl}/about-me");
            await page.Locator("[data-testid='about-save']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            await WaitUntilAsync(() => _profiles.Loaded.Count >= 2);
            await page.Locator("[data-testid='about-name']").PressSequentiallyAsync("Sam Example");
            await page.CheckAsync("[data-testid='about-formality-informal']");
            await page.SelectOptionAsync("[data-testid='about-language']", "de");

            await page.ClickAsync("[data-testid='about-save']");

            await page.Locator("[data-testid='about-saved']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            var (username, input) = Assert.Single(_profiles.Saved);
            Assert.Equal(User, username);
            Assert.Equal("Sam Example", input.DisplayName);
            Assert.Equal(Geef.Atelier.Core.Domain.Profiles.Formality.Informal, input.Formality);
            Assert.Equal("de", input.Language);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    private const string WideElementsScript = """
        root => {
            const width = document.documentElement.clientWidth;
            return [...document.querySelectorAll(root + ' *')]
                .filter(e => e.getBoundingClientRect().right > width + 1)
                .slice(0, 8)
                .map(e => e.tagName.toLowerCase() + (e.className && typeof e.className === 'string' ? '.' + e.className.trim().split(/\s+/).join('.') : '') + ' ' + Math.round(e.getBoundingClientRect().right))
                .join(', ');
        }
        """;

    private static async Task ClickUntilAsync(IPage page, string selector, Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!done())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Clicking {selector} had no effect.");
            await page.ClickAsync(selector);
            await Task.Delay(300);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition not met in time.");
            await Task.Delay(50);
        }
        await Task.Delay(300);
    }

    private static Task WaitForAsync(IPage page, string condition) =>
        page.WaitForFunctionAsync(condition, null, new PageWaitForFunctionOptions { Timeout = 15_000 });
}
