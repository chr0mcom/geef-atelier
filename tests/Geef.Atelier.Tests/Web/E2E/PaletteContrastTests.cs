using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Tests.Web.Components.Orders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Geef.Atelier.Tests.Web.E2E;

/// <summary>
/// All three palettes in a real browser: every text of a result with a table, a quote, a link and code on its sheet, of the user chip in
/// the navigation, of the filters of "My work" and of the run list, of the findings of a run with all four severities and of the
/// sign-in page keeps a contrast of at least 4.5:1.
/// </summary>
[Collection("Playwright")]
public sealed class PaletteContrastTests(PlaywrightFixture pw, PostgresFixture pg) : IAsyncLifetime
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
    [InlineData("vellum")]
    [InlineData("noir")]
    [InlineData("petrol")]
    public async Task Result_navigation_and_filters_are_readable(string palette)
    {
        var view = _orders.Add("test-user", OrderTestData.View(OrderDisplayState.Delivered));
        _orders.SetResult(OrderTestData.Result(view, text: Text));
        await using var context = await NewContextAsync(_host!, palette);
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{_host!.BaseUrl}/orders/{view.OrderId}");
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

    [Theory]
    [InlineData("vellum")]
    [InlineData("noir")]
    [InlineData("petrol")]
    public async Task The_severity_marks_of_a_run_are_readable(string palette)
    {
        var runId = await StoreRunWithEverySeverityAsync();
        await using var context = await NewContextAsync(_host!, palette);
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{_host!.BaseUrl}/runs/{runId}");
        var head = page.Locator(".iter-head").First;
        await head.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        await Assertions.Expect(page.Locator(".finding .severity")).ToHaveCountAsync(4);
        var open = page.Locator(".iter.open");
        for (var attempt = 0; attempt < 50 && await open.CountAsync() == 0; attempt++)
        {
            await head.ClickAsync();
            await Task.Delay(200);
        }
        await Assertions.Expect(page.Locator(".finding .severity").First).ToBeVisibleAsync();
        await page.AssertContrastAsync($"run-{palette}", "body");
    }

    [Theory]
    [InlineData("vellum")]
    [InlineData("noir")]
    [InlineData("petrol")]
    public async Task The_sign_in_page_is_readable(string palette)
    {
        await using var host = await WebTestHost.StartAsync(pg, authenticated: false);
        await using var context = await NewContextAsync(host, palette);
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{host.BaseUrl}/login");
        await page.FillAsync("input#username", "wrong-user");
        await page.FillAsync("input#password", "wrong-password");
        await page.ClickAsync("button.btn-login");
        await page.Locator(".login-error").WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        await page.AssertContrastAsync($"login-{palette}", "body");
    }

    private async Task<IBrowserContext> NewContextAsync(WebTestHost host, string palette)
    {
        var context = await pw.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1366, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce,
        });
        await context.AddCookiesAsync([new Cookie { Name = "Atelier.Theme", Value = palette, Url = host.BaseUrl }]);
        return context;
    }

    private async Task<Guid> StoreRunWithEverySeverityAsync()
    {
        await using var scope = _host!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtelierDbContext>();
        var now = DateTimeOffset.UtcNow;
        var run = new RunEntity
        {
            Id = Guid.NewGuid(), CreatedAt = now, CompletedAt = now, Status = RunStatus.Completed,
            BriefingText = "Write an invitation.", CreatedByUser = "test-user", FinalText = "Dear all, please come.",
        };
        var iteration = new IterationEntity { Id = Guid.NewGuid(), RunId = run.Id, IterationNumber = 1, ArtifactText = "Dear all, please come.", CreatedAt = now };
        db.Runs.Add(run);
        db.Iterations.Add(iteration);
        foreach (var severity in Enum.GetValues<FindingSeverity>())
            db.Findings.Add(new FindingEntity
            {
                Id = Guid.NewGuid(), IterationId = iteration.Id, ReviewerName = "deterministic-checks", Severity = severity,
                Message = $"A finding of severity {severity}.", CreatedAt = now,
            });
        await db.SaveChangesAsync();
        return run.Id;
    }
}
