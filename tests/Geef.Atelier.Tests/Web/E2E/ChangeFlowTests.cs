using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Tests.Web.Components.Orders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Geef.Atelier.Tests.Web.E2E;

/// <summary>
/// Changing a text in a real browser: the card "Change something" sends what the person clicked and typed, and the page with the card,
/// the version line, the list of versions and the opened changes fits a phone screen without sideways scrolling in the dark and the
/// light look; so does the page while a new version is written. The order service is an in-memory fake.
/// </summary>
[Collection("Playwright")]
public sealed class ChangeFlowTests(PlaywrightFixture pw, PostgresFixture pg) : IAsyncLifetime
{
    private const string User = "test-user";

    private const string LongWord = "Donaudampfschifffahrtsgesellschaftskapitänsmützenabzeichensammlung";

    private static readonly string FirstText =
        $"Liebe Eltern,\n\nder Elternabend der Klasse 3b ist am **12. Mai** um 19 Uhr in der Aula. {LongWord}.\n\nAnmeldung unter https://schule.example/elternabend/anmeldung/klasse-3b/fruehjahr-2027?quelle=brief\n\nWir freuen uns auf Sie.";

    private static readonly string SecondText = FirstText.Replace("12. Mai", "5. Mai", StringComparison.Ordinal);

    private readonly FakeOrderService _orders = new();
    private readonly FakeOrderExports _exports = new();
    private WebTestHost? _host;

    public async Task InitializeAsync() =>
        _host = await WebTestHost.StartAsync(pg, initialGateCount: 100, configureServices: s =>
        {
            s.AddSingleton<IOrderService>(_orders);
            s.AddSingleton<IOrderExportService>(_exports);
            s.PostConfigure<OrderOptions>(o => o.Revise = ReviseMode.On);
        });

    public async Task DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
    }

    private static OrderRunView RunView(int version, RunStatus status, RunOutcome? outcome, int? words, string? asked = null) =>
        new(Guid.NewGuid(), version, RunKind.Standard, status, outcome, OrderTestData.Start.AddMinutes(version * 5), OrderTestData.Start.AddMinutes(version * 5),
            status is RunStatus.Pending or RunStatus.Running ? null : OrderTestData.Start.AddMinutes(version * 5 + 2), words, asked);

    private Guid AddOrder(bool writing)
    {
        var runs = new[]
        {
            RunView(1, RunStatus.Completed, RunOutcome.Converged, 40),
            writing
                ? RunView(2, RunStatus.Running, null, null, $"Shorter, The meeting is on 5 May, not 12 May. {LongWord}")
                : RunView(2, RunStatus.Completed, RunOutcome.Converged, 40, $"The meeting is on 5 May, not 12 May. {LongWord}"),
        };
        var state = writing ? OrderDisplayState.Working : OrderDisplayState.Delivered;
        var view = _orders.Add(User, OrderTestData.View(state, hasText: !writing) with { Runs = runs, CurrentRun = runs[^1] });
        var shown = writing ? 1 : 2;
        _orders.SetResult(OrderTestData.Result(view, text: shown == 1 ? FirstText : SecondText,
            note: writing ? string.Format(RevisionLines.StillWritingFormat, 1) : null) with { Version = shown, LatestVersion = 2 });
        _exports.Texts[(view.OrderId, 1)] = FirstText;
        _exports.Texts[(view.OrderId, 2)] = SecondText;
        return view.OrderId;
    }

    private async Task<IPage> OpenAsync(Guid orderId, string theme, int width)
    {
        var context = await pw.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = width, Height = 800 } });
        await context.AddCookiesAsync([new Cookie { Name = "Atelier.Theme", Value = theme, Url = _host!.BaseUrl }]);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{_host.BaseUrl}/orders/{orderId}");
        await page.Locator("[data-testid='order-result']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        return page;
    }

    [Fact]
    public async Task The_card_sends_what_was_clicked_and_typed()
    {
        var orderId = AddOrder(writing: false);
        var page = await OpenAsync(orderId, "vellum", 1200);
        try
        {
            await page.Locator("[data-testid='order-change-open']").ClickAsync();
            await page.Locator("[data-quick='Shorter']").ClickAsync();
            await page.Locator("[data-quick='Friendlier']").ClickAsync();
            Assert.Equal("true", await page.Locator("[data-quick='Friendlier']").GetAttributeAsync("aria-pressed"));
            await page.Locator("[data-testid='order-change-text']").FillAsync("The meeting is on 5 May, not 12 May.");
            await page.Locator("[data-testid='order-change-make']").ClickAsync();

            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while (_orders.Revised.IsEmpty && DateTimeOffset.UtcNow < deadline)
                await Task.Delay(100);
            var request = Assert.Single(_orders.Revised);
            Assert.Equal((orderId, User, "The meeting is on 5 May, not 12 May."), (request.OrderId, request.RequestingUsername, request.Change));
            Assert.Equal([RevisionQuick.Shorter, RevisionQuick.Friendlier], request.Quick);
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [Theory]
    [InlineData("noir")]
    [InlineData("vellum")]
    public async Task The_sentence_about_a_clearly_longer_version_stands_above_the_text_and_fits_a_phone(string theme)
    {
        var orderId = AddOrder(writing: false);
        var shown = await _orders.GetResultAsync(orderId, User);
        _orders.SetResult(shown! with { LengthNotice = RevisionLines.LongerThanBefore });
        var page = await OpenAsync(orderId, theme, 360);
        try
        {
            var notice = page.Locator("[data-testid='order-longer-notice']");
            await notice.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            Assert.Equal(RevisionLines.LongerThanBefore, (await notice.InnerTextAsync()).Trim());
            Assert.True(await page.EvaluateAsync<bool>(
                "() => !!(document.querySelector(\"[data-testid='order-longer-notice']\").compareDocumentPosition(document.querySelector('.order-result-sheet')) & Node.DOCUMENT_POSITION_FOLLOWING)"),
                "the sentence stands above the text");

            await AssertFitsAsync(page);
            await ScreenshotAsync(page, $"longer-notice-phone-{theme}.png");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [Theory]
    [InlineData("noir")]
    [InlineData("vellum")]
    public async Task The_page_with_card_versions_and_changes_fits_a_phone(string theme)
    {
        var orderId = AddOrder(writing: false);
        var page = await OpenAsync(orderId, theme, 360);
        try
        {
            await page.Locator("[data-testid='order-change-open']").ClickAsync();
            await page.Locator("[data-testid='order-change-card']").WaitForAsync();
            await page.Locator("[data-version='2'] [data-testid='order-version-toggle']").ClickAsync();
            await page.Locator("[data-testid='order-diff-added']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
            foreach (var same in await page.Locator("[data-testid='order-diff-same'] summary").AllAsync())
                await same.ClickAsync();
            await page.Locator("[data-version='2'] [data-testid='order-version-download'] summary").ClickAsync();

            await AssertFitsAsync(page);
            Assert.True(await page.Locator("[data-testid='order-diff-removed']").IsVisibleAsync());
            await ScreenshotAsync(page, $"change-phone-{theme}.png");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [Theory]
    [InlineData("noir")]
    [InlineData("vellum")]
    public async Task While_a_new_version_is_written_the_page_fits_a_phone_and_keeps_the_text(string theme)
    {
        var orderId = AddOrder(writing: true);
        var page = await OpenAsync(orderId, theme, 360);
        try
        {
            await page.Locator("[data-testid='order-progress']").WaitForAsync();
            Assert.Equal(string.Format(RevisionLines.StillWritingFormat, 1), (await page.Locator("[data-testid='order-version-now'] span").TextContentAsync())?.Trim());
            Assert.Contains("12. Mai", await page.Locator("[data-testid='order-result']").TextContentAsync());
            Assert.Equal(0, await page.Locator("[data-testid='order-change-open']").CountAsync());

            await AssertFitsAsync(page);
            await ScreenshotAsync(page, $"change-writing-phone-{theme}.png");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [Theory]
    [InlineData("noir")]
    [InlineData("vellum")]
    public async Task Notes_of_earlier_versions_open_with_the_keyboard_and_fit_a_phone(string theme)
    {
        var runs = new[]
        {
            RunView(1, RunStatus.Completed, RunOutcome.Converged, 40),
            RunView(2, RunStatus.Completed, RunOutcome.Converged, 40, "The meeting is on 5 May, not 12 May."),
        };
        var view = _orders.Add(User, OrderTestData.View(OrderDisplayState.Delivered, hasText: true) with { Runs = runs, CurrentRun = runs[^1] });
        _orders.SetResult(OrderTestData.Result(view, text: SecondText) with
        {
            Version = 2,
            LatestVersion = 2,
            AssumptionsVersion = 1,
            WriterAssumptions = [$"Der Text wurde nur beim Datum geändert. {LongWord}."],
            EarlierOpenPoints = Enumerable.Range(1, 12).Select(i => new OrderEarlierPoint(1, $"Punkt {i}: Der Name der Klassenlehrkraft fehlt noch. {LongWord}.")).ToList(),
        });
        _exports.Texts[(view.OrderId, 1)] = FirstText;
        _exports.Texts[(view.OrderId, 2)] = SecondText;
        var page = await OpenAsync(view.OrderId, theme, 360);
        try
        {
            var details = page.Locator("[data-testid='order-open-points'] [data-testid='order-earlier-details']");
            var summary = details.Locator("summary");
            Assert.Equal("12 notes about version 1", (await summary.TextContentAsync())?.Trim());
            Assert.False(await details.Locator("[data-testid='order-earlier-point']").First.IsVisibleAsync());
            Assert.Equal(RevisionLines.ChangeNotesLead, (await page.Locator("[data-testid='order-assumptions-change-lead']").TextContentAsync())?.Trim());
            await AssertFitsAsync(page);

            await summary.FocusAsync();
            await page.Keyboard.PressAsync("Enter");

            await details.Locator("[data-testid='order-earlier-point']").Last.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            Assert.Equal(12, await details.Locator("[data-testid='order-earlier-point']").CountAsync());
            await AssertFitsAsync(page);
            await ScreenshotAsync(page, $"change-earlier-notes-phone-{theme}.png");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    private static async Task AssertFitsAsync(IPage page)
    {
        var overflow = await page.EvaluateAsync<int>("document.documentElement.scrollWidth - document.documentElement.clientWidth");
        var wide = await page.EvaluateAsync<string>(WideElementsScript);
        Assert.True(overflow <= 0, $"The page scrolls sideways by {overflow} px on a phone: {wide}");
        Assert.True(wide.Length == 0, $"Elements stick out on a phone: {wide}");
    }

    private static async Task ScreenshotAsync(IPage page, string name)
    {
        if (Environment.GetEnvironmentVariable("ATELIER_SCREENSHOT_DIR") is { Length: > 0 } dir)
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(dir, name), FullPage = true });
    }

    private const string WideElementsScript = """
        () => {
            const width = document.documentElement.clientWidth;
            return [...document.querySelectorAll('[data-testid="order-page"] *')]
                .filter(e => e.getBoundingClientRect().width > 0 && e.getBoundingClientRect().right > width + 1)
                .slice(0, 8)
                .map(e => e.tagName.toLowerCase() + (e.className && typeof e.className === 'string' ? '.' + e.className.trim().split(/\s+/).join('.') : '') + ' ' + Math.round(e.getBoundingClientRect().right))
                .join(', ');
        }
        """;
}
