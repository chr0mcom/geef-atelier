using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Web.Components;
using Geef.Atelier.Web.Components.Pages;
using Geef.Atelier.Web.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Components.Orders;

/// <summary>The order page before writing: a short wait while the request is read, then the questions, then the usual progress with a summary.</summary>
public sealed class OrderDetailIntakeTests : TestContext
{
    private readonly FakeOrderService _orders = new();
    private readonly ScriptedOrderFeed _feed = new();

    public OrderDetailIntakeTests()
    {
        Services.AddSingleton<IOrderService>(_orders);
        Services.AddSingleton<IRunUpdateFeed>(_feed);
        Services.AddScoped<CircuitDataGate>();
        Services.AddOptions();
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("alice");
    }

    [Fact]
    public void WhileTheRequestIsReadItShowsTheWaitInsteadOfTheSteps()
    {
        var order = _orders.Add("alice", OrderTestData.Analyzing());

        var cut = Render(order.OrderId);

        Assert.NotNull(cut.Find("[data-testid='intake-waiting']"));
        Assert.Empty(cut.FindAll("[data-testid='order-progress']"));
        Assert.Empty(cut.FindAll("[data-testid='clarify-card']"));
        Assert.NotNull(cut.Find("[data-testid='intake-waiting'] [data-testid='order-stop']"));
        Assert.Equal("Reading your request.", cut.Find("[data-testid='order-intake-status']").TextContent);
        Assert.Equal("polite", cut.Find("[data-testid='order-intake-status']").GetAttribute("aria-live"));
    }

    [Fact]
    public void SkipAndStartNowStartsAndShowsTheProgress()
    {
        var order = _orders.Add("alice", OrderTestData.Analyzing());
        var cut = Render(order.OrderId);

        cut.Find("[data-testid='intake-skip']").Click();

        Assert.Equal((order.OrderId, "alice"), Assert.Single(_orders.Skipped));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='order-progress']")));
        Assert.Empty(cut.FindAll("[data-testid='intake-waiting']"));
    }

    [Fact]
    public void APreparingOrderOutsideTheIntakeKeepsTheSteps()
    {
        var order = _orders.Add("alice", OrderTestData.View(OrderDisplayState.Preparing));

        var cut = Render(order.OrderId);

        Assert.Empty(cut.FindAll("[data-testid='intake-waiting']"));
        Assert.NotNull(cut.Find("[data-testid='order-progress']"));
    }

    [Fact]
    public void WaitingForAnswersShowsTheCardWithoutTheBannerAndTheSteps()
    {
        var order = _orders.Add("alice", OrderTestData.Asking(OrderTestData.Clarification()));

        var cut = Render(order.OrderId);

        Assert.NotNull(cut.Find("[data-testid='clarify-card']"));
        Assert.Empty(cut.FindAll("[data-testid='order-banner']"));
        Assert.Empty(cut.FindAll("[data-testid='order-progress']"));
        Assert.NotNull(cut.Find("[data-testid='clarify-card'] [data-testid='order-stop']"));
        Assert.Equal("Before I start: one quick question.", cut.Find("[data-testid='order-intake-status']").TextContent);
        Assert.Equal("needs-input", cut.Find("[data-testid='order-state']").GetAttribute("data-state"));
    }

    [Fact]
    public void StopWorksWhileWaitingForAnswers()
    {
        var order = _orders.Add("alice", OrderTestData.Asking(OrderTestData.Clarification()));
        var cut = Render(order.OrderId);

        cut.Find("[data-testid='order-stop']").Click();

        Assert.Equal(order.OrderId, Assert.Single(_orders.Cancelled));
    }

    [Fact]
    public void AnsweringStartsTheWritingAndShowsTheSummaryAboveTheSteps()
    {
        var order = _orders.Add("alice", OrderTestData.Asking(OrderTestData.Clarification()));
        var cut = Render(order.OrderId);

        cut.Find("[data-id='short']").Click();
        cut.Find("[data-testid='clarify-start']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='order-progress']")));
        Assert.Empty(cut.FindAll("[data-testid='clarify-card']"));
        var markup = cut.Markup;
        Assert.True(markup.IndexOf("data-testid=\"clarify-summary\"", StringComparison.Ordinal)
                    < markup.IndexOf("data-testid=\"order-progress\"", StringComparison.Ordinal));
        Assert.Contains("Kurz (etwa 150 Wörter)", cut.Find("[data-testid='clarify-summary-answer']").TextContent);
        Assert.Equal("queued", cut.Find("[data-testid='order-state']").GetAttribute("data-state"));
    }

    [Fact]
    public void TheCardActsAsTheSignedInPersonNotAsAnAdministrator()
    {
        var order = _orders.Add("alice", OrderTestData.Asking(OrderTestData.Clarification()));
        var cut = Render(order.OrderId);

        cut.Find("[data-id='short']").Click();
        cut.Find("[data-testid='clarify-start']").Click();

        var request = Assert.Single(_orders.Answered);
        Assert.Equal((order.OrderId, "alice"), (request.OrderId, request.RequestingUsername));
    }

    [Fact]
    public void JustStartOnTheCardActsAsTheSignedInPerson()
    {
        var order = _orders.Add("alice", OrderTestData.Asking(OrderTestData.Clarification()));
        var cut = Render(order.OrderId);

        cut.Find("[data-testid='clarify-just-start']").Click();

        Assert.Equal((order.OrderId, "alice"), Assert.Single(_orders.Skipped));
    }

    [Fact]
    public void ANoticeFromTheServiceIsShownAfterTheStart()
    {
        _orders.StartNotices = ["This order has already started."];
        var order = _orders.Add("alice", OrderTestData.Asking(OrderTestData.Clarification()));
        var cut = Render(order.OrderId);

        cut.Find("[data-testid='clarify-just-start']").Click();

        cut.WaitForAssertion(() => Assert.Equal("This order has already started.", cut.Find("[data-testid='order-intake-notice']").TextContent));
    }

    [Fact]
    public void AStartedOrderShowsTheSummaryAlsoUnderHowItWasMade()
    {
        var view = OrderTestData.View(OrderDisplayState.Delivered) with { Clarification = OrderTestData.Clarification(outcome: IntakeOutcome.Clarified) };
        _orders.Add("alice", view);
        _orders.SetResult(OrderTestData.Result(view));

        var cut = Render(view.OrderId);

        var summary = Assert.Single(cut.FindAll("[data-testid='clarify-summary']"));
        Assert.NotNull(summary.Closest("[data-testid='order-how-made']"));
        Assert.Contains("Nothing needed to be asked", summary.TextContent);
    }

    [Fact]
    public void AnOrderWithoutClarificationShowsNoSummary()
    {
        var view = _orders.Add("alice", OrderTestData.View(OrderDisplayState.Working));

        var cut = Render(view.OrderId);

        Assert.Empty(cut.FindAll("[data-testid='clarify-summary']"));
        Assert.Equal(string.Empty, cut.Find("[data-testid='order-intake-status']").TextContent);
    }

    [Fact]
    public async Task ALiveUpdateTurnsTheWaitIntoTheCard()
    {
        var order = _orders.Add("alice", OrderTestData.Analyzing());
        var cut = Render(order.OrderId);
        await WaitUntilAsync(() => _feed.OrderHandler is not null);

        _orders.Update(order.OrderId, _ => OrderTestData.Asking(OrderTestData.Clarification(), orderId: order.OrderId));
        await _feed.OrderHandler!(order.OrderId);

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='clarify-card']")));
        Assert.Empty(cut.FindAll("[data-testid='intake-waiting']"));
    }

    [Fact]
    public async Task TheTabTitleSaysThatAnswersAreNeeded()
    {
        var title = JSInterop.SetupModule("./js/page-title.js");
        title.SetupVoid("set", _ => true).SetVoidResult();
        var order = _orders.Add("alice", OrderTestData.Asking(OrderTestData.Clarification(), title: "Summer party"));

        var cut = Render(order.OrderId);

        cut.WaitForAssertion(() => Assert.Contains(title.Invocations["set"], i => (string)i.Arguments[0]! == "Needs your answers – Summer party — Atelier"));
        await Task.CompletedTask;
    }

    private IRenderedComponent<OrderDetail> Render(Guid orderId) =>
        RenderComponent<OrderDetail>(p => p.Add(c => c.OrderId, orderId));

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
