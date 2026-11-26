using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Web.Components;
using Geef.Atelier.Web.Components.Pages;
using Geef.Atelier.Web.Notifications;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Components.Orders;

/// <summary>
/// The order page: plain progress while it works, then the result first, open points, files, warnings, and the
/// history folded away under "How it was made". Live updates follow the pattern of the run pages.
/// </summary>
public sealed class OrderDetailTests : TestContext
{
    private readonly FakeOrderService _orders = new();
    private readonly ScriptedOrderFeed _feed = new();
    private readonly TestAuthorizationContext _auth;

    public OrderDetailTests()
    {
        Services.AddSingleton<IOrderService>(_orders);
        Services.AddSingleton<IRunUpdateFeed>(_feed);
        Services.AddScoped<CircuitDataGate>();
        Services.AddOptions();
        JSInterop.Mode = JSRuntimeMode.Loose;
        _auth = this.AddTestAuthorization();
        _auth.SetAuthorized("alice");
    }

    public static TheoryData<OrderDisplayState> AllStates => new(Enum.GetValues<OrderDisplayState>());

    [Theory]
    [MemberData(nameof(AllStates))]
    public void EveryStateShowsItsLabelAndTheRightBanner(OrderDisplayState state)
    {
        var view = _orders.Add("alice", OrderTestData.View(state));
        if (view.Display.HasText)
            _orders.SetResult(OrderTestData.Result(view));

        var cut = Render(view.OrderId);

        var badge = cut.Find("[data-testid='order-state']");
        Assert.Equal(ExpectedLabel(state), badge.TextContent.Trim());
        var busy = state is OrderDisplayState.Preparing or OrderDisplayState.NeedsInput or OrderDisplayState.Queued or OrderDisplayState.Working;
        Assert.Equal(busy, cut.FindAll("[data-testid='order-progress']").Count == 1);
        var banners = cut.FindAll("[data-testid='order-banner']");
        switch (state)
        {
            case OrderDisplayState.Delivered:
                Assert.Contains("Your text is ready.", Assert.Single(banners).TextContent);
                break;
            case OrderDisplayState.DeliveredWithOpenPoints:
                Assert.Contains("Your text is ready – please check it.", Assert.Single(banners).TextContent);
                break;
            case OrderDisplayState.Failed:
                Assert.Contains("No text was written.", Assert.Single(banners).TextContent);
                Assert.Equal($"/start?from={view.OrderId}", cut.Find("[data-testid='order-try-again']").GetAttribute("href"));
                break;
            case OrderDisplayState.Stopped:
                Assert.Contains("stopped before a text was written", Assert.Single(banners).TextContent);
                Assert.Equal($"/start?from={view.OrderId}", cut.Find("[data-testid='order-try-again']").GetAttribute("href"));
                break;
            case OrderDisplayState.NeedsInput:
                Assert.Contains("We need a few answers from you.", Assert.Single(banners).TextContent);
                break;
            default:
                Assert.Empty(banners);
                break;
        }
    }

    [Theory]
    [MemberData(nameof(AllStates))]
    public void WithATextThePageNeverSaysSomethingWentWrong(OrderDisplayState state)
    {
        var view = _orders.Add("alice", OrderTestData.View(state, hasText: true));
        _orders.SetResult(OrderTestData.Result(view));

        var cut = Render(view.OrderId);

        Assert.DoesNotContain("something went wrong", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("No text was written", cut.Markup);
        Assert.NotNull(cut.Find("[data-testid='order-result']"));
    }

    [Fact]
    public void TheResultComesBeforeHowItWasMadeAndOffersThreeDownloads()
    {
        var view = _orders.Add("alice", OrderTestData.View(OrderDisplayState.Delivered));
        _orders.SetResult(OrderTestData.Result(view, text: "# Delay\n\nDear customer, your order is late."));

        var cut = Render(view.OrderId);

        var markup = cut.Markup;
        Assert.True(markup.IndexOf("data-testid=\"order-result\"", StringComparison.Ordinal)
                    < markup.IndexOf("data-testid=\"order-how-made\"", StringComparison.Ordinal));
        Assert.Contains("Dear customer, your order is late.", cut.Find("[data-testid='order-result']").TextContent);
        Assert.Equal($"/orders/{view.OrderId}/export/docx?version=1", cut.Find("[data-testid='order-download-docx']").GetAttribute("href"));
        Assert.Equal($"/orders/{view.OrderId}/export/pdf?version=1", cut.Find("[data-testid='order-download-pdf']").GetAttribute("href"));
        Assert.Equal($"/orders/{view.OrderId}/export/md?version=1", cut.Find("[data-testid='order-download-md']").GetAttribute("href"));
        Assert.Equal("details", cut.Find("[data-testid='order-how-made']").TagName.ToLowerInvariant());
        Assert.False(cut.Find("[data-testid='order-how-made']").HasAttribute("open"));
    }

    [Fact]
    public void CopyPutsTheTextOnTheClipboard()
    {
        var view = _orders.Add("alice", OrderTestData.View(OrderDisplayState.Delivered));
        _orders.SetResult(OrderTestData.Result(view, text: "Hello there."));
        var cut = Render(view.OrderId);

        cut.Find("[data-testid='order-copy']").Click();

        Assert.Equal("Hello there.", JSInterop.VerifyInvoke("navigator.clipboard.writeText").Arguments[0]);
        Assert.Contains("Copied", cut.Find("[data-testid='order-copy']").TextContent);
    }

    [Fact]
    public void OpenPointsListPlaceholdersAndOpenChecks()
    {
        var view = _orders.Add("alice", OrderTestData.View(OrderDisplayState.DeliveredWithOpenPoints));
        _orders.SetResult(OrderTestData.Result(view,
            placeholders: ["[delivery date]", "[Name der Ansprechperson]"],
            openChecks: [new DraftCheckResult(DraftCheckCodes.Length, false, true, "The text has 263 words. The request asks for at most 200 words.", "max. 200 Wörter")],
            sourcesUsed: [new OrderSourceUsageView("bill.pdf", SourceUsageMode.FullText, 160_485, null), new OrderSourceUsageView("big.pdf", SourceUsageMode.FullTextWithMap, 500_000, "1-40")],
            note: "Some checks are still open."));

        var cut = Render(view.OrderId);

        Assert.Equal(new[] { "[delivery date]", "[Name der Ansprechperson]" }, cut.FindAll("[data-testid='order-placeholder']").Select(e => e.TextContent));
        Assert.Contains("263 words", cut.Find("[data-testid='order-open-check']").TextContent);
        var files = cut.FindAll("[data-testid='order-source-used']").Select(e => e.TextContent).ToList();
        Assert.Contains("read completely · 160,485 characters", files[0]);
        Assert.Contains("read with a table of contents · 500,000 characters · pages 1-40", files[1]);
        Assert.Equal("Some checks are still open.", cut.Find("[data-testid='order-note']").TextContent);
        Assert.Contains("One check is still open", cut.Find("[data-testid='order-how-checks']").TextContent);
    }

    [Fact]
    public void AHintThatNeverBlocksIsShownApartFromTheOpenChecks()
    {
        var view = _orders.Add("alice", OrderTestData.View(OrderDisplayState.Delivered));
        _orders.SetResult(OrderTestData.Result(view,
            openChecks: [new DraftCheckResult(DraftCheckCodes.Preamble, false, false, "The text may start with a remark to the reader (\"Gerne, hier ist der Beitrag:\").", null)]));

        var cut = Render(view.OrderId);

        Assert.Contains("Gerne, hier ist der Beitrag:", cut.Find("[data-testid='order-hint']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='order-open-check']"));
        Assert.DoesNotContain("may not yet do everything", cut.Markup);
        Assert.Contains("All automatic checks passed.", cut.Find("[data-testid='order-how-checks']").TextContent);
    }

    [Fact]
    public void WithoutOpenPointsThereIsNoOpenPointsBlockAndNoInventedAssumptions()
    {
        var view = _orders.Add("alice", OrderTestData.View(OrderDisplayState.Delivered));
        _orders.SetResult(OrderTestData.Result(view));

        var cut = Render(view.OrderId);

        Assert.Empty(cut.FindAll("[data-testid='order-open-points']"));
        Assert.DoesNotContain("Assumptions", cut.Markup);
        Assert.Contains("All automatic checks passed.", cut.Find("[data-testid='order-how-checks']").TextContent);
    }

    [Fact]
    public void WhileWorkingItShowsStepsElapsedTimeAndTheHint()
    {
        var view = _orders.Add("alice", OrderTestData.View(OrderDisplayState.Working));

        var cut = Render(view.OrderId);

        var steps = cut.FindAll("[data-testid='order-step']");
        Assert.Equal(new[] { "Writing", "Checking", "Done" }, steps.Select(s => s.QuerySelector(".order-step-label")!.TextContent));
        Assert.Equal("active", steps[0].GetAttribute("data-status"));
        Assert.Matches(@"^\d+:\d\d$", cut.Find("[data-testid='order-elapsed']").TextContent);
        Assert.Equal("Usually about 5 minutes. You can close this page – your text will be waiting in My work.",
            cut.Find("[data-testid='order-progress-hint']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='order-result']"));
    }

    [Fact]
    public void StopStopsTheOrder()
    {
        var view = _orders.Add("alice", OrderTestData.View(OrderDisplayState.Working));
        var cut = Render(view.OrderId);

        cut.Find("[data-testid='order-stop']").Click();

        Assert.Equal(view.OrderId, Assert.Single(_orders.Cancelled));
        Assert.Equal("alice", Assert.Single(_orders.CancelledFor));
        Assert.Equal("stopped", cut.Find("[data-testid='order-state']").GetAttribute("data-state"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASignInWithoutUserNameShowsNothingAndAsksToSignInAgain(bool withEmptyName)
    {
        var view = _orders.Add("bob", OrderTestData.View(OrderDisplayState.Delivered, title: "Bob's private letter"));
        if (withEmptyName)
            _auth.SetAuthorized(string.Empty);
        else
            this.SignInWithoutUserName();

        var cut = Render(view.OrderId);

        Assert.Contains(OwnerScope.UnknownUserMessage, cut.Find("[data-testid='order-unknown-user']").TextContent);
        Assert.DoesNotContain("private letter", cut.Markup);
        Assert.Equal(0, _orders.Calls);
        Assert.Empty(cut.FindAll("[data-testid='order-stop']"));
    }

    [Fact]
    public void AStopThatDidNotWorkIsExplained()
    {
        _orders.CancelResult = false;
        var view = _orders.Add("alice", OrderTestData.View(OrderDisplayState.Working));
        var cut = Render(view.OrderId);

        cut.Find("[data-testid='order-stop']").Click();

        Assert.Contains("could not be stopped", cut.Find("[data-testid='order-action-error']").TextContent);
    }

    [Fact]
    public void AnotherPersonsOrderIsNotShown()
    {
        var view = _orders.Add("bob", OrderTestData.View(OrderDisplayState.Delivered, title: "Bob's private letter"));

        var cut = Render(view.OrderId);

        Assert.NotNull(cut.Find("[data-testid='order-not-found']"));
        Assert.DoesNotContain("private letter", cut.Markup);
        Assert.Equal("/work", cut.Find("[data-testid='order-not-found'] a").GetAttribute("href"));
    }

    [Fact]
    public void AdministratorsSeeEveryOrder()
    {
        _auth.SetAuthorized("root");
        _auth.SetRoles("admin");
        var view = _orders.Add("bob", OrderTestData.View(OrderDisplayState.Working, title: "Bob's letter"));

        var cut = Render(view.OrderId);

        Assert.Equal("Bob's letter", cut.Find("[data-testid='order-title']").TextContent);
    }

    [Fact]
    public void ALoadErrorOffersToTryAgain()
    {
        var view = _orders.Add("alice", OrderTestData.View(OrderDisplayState.Working));
        _orders.GetError = new InvalidOperationException("db");

        var cut = Render(view.OrderId);
        Assert.Contains("could not be loaded", cut.Find("[data-testid='order-load-failed']").TextContent);

        _orders.GetError = null;
        cut.Find("[data-testid='order-reload']").Click();

        Assert.Equal("working", cut.Find("[data-testid='order-state']").GetAttribute("data-state"));
    }

    [Fact]
    public void WarningsAndHowItWasMadeAreShown()
    {
        var runId = Guid.NewGuid();
        var warning = new RunWarningEntity
        {
            Id = Guid.NewGuid(), RunId = runId, Source = "grounding", Code = "grounding.source_warning",
            Message = "Some pages of bill.pdf have no text.", CreatedAt = OrderTestData.Start,
        };
        var view = _orders.Add("alice", OrderTestData.View(OrderDisplayState.Delivered, warnings: [warning],
            sources: [OrderTestData.Source("bill.pdf", 160_485, 80) with { PageSelection = "1-40" }]));
        _orders.SetResult(OrderTestData.Result(view));

        var cut = Render(view.OrderId);

        Assert.Contains("Some pages of bill.pdf have no text.", cut.Find("[data-testid='run-warning']").TextContent);
        Assert.Contains("The result is ready, but not everything went as planned.", cut.Find("[data-testid='run-warnings-intro']").TextContent);
        Assert.Equal("bill.pdf · 160,485 characters · 80 pages · pages used: 1-40", cut.Find("[data-testid='order-how-file']").TextContent);
        Assert.Contains("Version 1", cut.Find("[data-testid='order-how-version']").TextContent);
        Assert.Equal(view.RequestText, cut.Find("[data-testid='order-how-request']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='order-technical-details']"));
    }

    [Fact]
    public void TheTechnicalDetailsAreLinkedForAdministrators()
    {
        _auth.SetRoles(OwnerScope.AdminRole);
        var view = _orders.Add("alice", OrderTestData.View(OrderDisplayState.Delivered));
        _orders.SetResult(OrderTestData.Result(view));

        var cut = Render(view.OrderId);

        Assert.Equal($"/runs/{view.CurrentRun!.RunId}", cut.Find("[data-testid='order-technical-details']").GetAttribute("href"));
    }

    [Fact]
    public void TheTechnicalDetailsAreLinkedForEveryoneWhenTheWorkshopSwitchIsOff()
    {
        Services.Configure<UiOptions>(o => o.WorkshopForAdminsOnly = false);
        var view = _orders.Add("alice", OrderTestData.View(OrderDisplayState.Delivered));
        _orders.SetResult(OrderTestData.Result(view));

        var cut = Render(view.OrderId);

        Assert.Equal($"/runs/{view.CurrentRun!.RunId}", cut.Find("[data-testid='order-technical-details']").GetAttribute("href"));
    }

    [Fact]
    public async Task ALiveUpdateShowsTheResult()
    {
        var view = _orders.Add("alice", OrderTestData.View(OrderDisplayState.Working));
        var cut = Render(view.OrderId);
        await WaitUntilAsync(() => _feed.OrderHandler is not null);

        var done = OrderTestData.View(OrderDisplayState.Delivered, orderId: view.OrderId);
        _orders.Update(view.OrderId, _ => done);
        _orders.SetResult(OrderTestData.Result(done, text: "The finished letter."));
        await _feed.OrderHandler!(view.OrderId);

        cut.WaitForAssertion(() => Assert.Contains("The finished letter.", cut.Find("[data-testid='order-result']").TextContent));
        Assert.Equal("delivered", cut.Find("[data-testid='order-state']").GetAttribute("data-state"));
    }

    [Fact]
    public async Task TheTabTitleFollowsTheState()
    {
        var title = JSInterop.SetupModule("./js/page-title.js");
        title.SetupVoid("set", _ => true).SetVoidResult();
        var view = _orders.Add("alice", OrderTestData.View(OrderDisplayState.Working, title: "Delay email"));
        var cut = Render(view.OrderId);
        await WaitUntilAsync(() => _feed.OrderHandler is not null);
        cut.WaitForAssertion(() => Assert.Contains(title.Invocations["set"], i => (string)i.Arguments[0]! == "Writing · Delay email — Atelier"));

        var done = OrderTestData.View(OrderDisplayState.Delivered, orderId: view.OrderId, title: "Delay email");
        _orders.Update(view.OrderId, _ => done);
        _orders.SetResult(OrderTestData.Result(done));
        await _feed.OrderHandler!(view.OrderId);

        cut.WaitForAssertion(() => Assert.Contains(title.Invocations["set"], i => (string)i.Arguments[0]! == "Ready · Delay email — Atelier"));
    }

    [Fact]
    public async Task ItReloadsOnceAfterSubscribingSoAFinalUpdateIsNotLost()
    {
        var view = _orders.Add("alice", OrderTestData.View(OrderDisplayState.Working));
        _feed.WatchHold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var cut = Render(view.OrderId);
        Assert.Equal("working", cut.Find("[data-testid='order-state']").GetAttribute("data-state"));

        var done = OrderTestData.View(OrderDisplayState.Delivered, orderId: view.OrderId);
        _orders.Update(view.OrderId, _ => done);
        _orders.SetResult(OrderTestData.Result(done));
        _feed.WatchHold.SetResult();

        cut.WaitForAssertion(() => Assert.Equal("delivered", cut.Find("[data-testid='order-state']").GetAttribute("data-state")));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ASubscriptionGrantedAfterThePageWasClosedIsReleased()
    {
        var view = _orders.Add("alice", OrderTestData.View(OrderDisplayState.Working));
        _feed.WatchHold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cut = Render(view.OrderId);

        DisposeComponents();
        _feed.WatchHold.SetResult();

        await WaitUntilAsync(() => _feed.OrderSubscriptions == 1);
        await WaitUntilAsync(() => _feed.OrderDisposals == 1);
        Assert.NotNull(cut);
    }

    [Fact]
    public async Task ALiveReloadAndStopNeverQueryAtTheSameTime()
    {
        var view = _orders.Add("alice", OrderTestData.View(OrderDisplayState.Working));
        var cut = Render(view.OrderId);
        await WaitUntilAsync(() => _feed.OrderHandler is not null);
        var callsBefore = _orders.Calls;
        _orders.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var liveReload = Task.Run(() => _feed.OrderHandler!(view.OrderId));
        await WaitUntilAsync(() => _orders.Calls == callsBefore + 1);
        var stop = cut.InvokeAsync(() => cut.Find("[data-testid='order-stop']").ClickAsync(new MouseEventArgs()));
        await Task.Delay(300);

        Assert.Equal(callsBefore + 1, _orders.Calls);
        _orders.Hold.SetResult();
        await liveReload.WaitAsync(TimeSpan.FromSeconds(10));
        await stop.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, _orders.MaxActive);
        Assert.Single(_orders.Cancelled);
    }

    private IRenderedComponent<OrderDetail> Render(Guid orderId) =>
        RenderComponent<OrderDetail>(p => p.Add(c => c.OrderId, orderId));

    private static string ExpectedLabel(OrderDisplayState state) => state switch
    {
        OrderDisplayState.Preparing => "Getting ready",
        OrderDisplayState.NeedsInput => "Needs your answers",
        OrderDisplayState.Queued => "Waiting to start",
        OrderDisplayState.Working => "Writing",
        OrderDisplayState.Delivered => "Ready",
        OrderDisplayState.DeliveredWithOpenPoints => "Ready – please check",
        OrderDisplayState.Failed => "No text – something went wrong",
        OrderDisplayState.Stopped => "Stopped",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition not met in time.");
            await Task.Delay(20);
        }
    }
}
