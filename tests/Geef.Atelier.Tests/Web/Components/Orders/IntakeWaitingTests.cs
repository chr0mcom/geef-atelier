using Bunit;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Web.Components;
using Geef.Atelier.Web.Components.UI.Orders;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Components.Orders;

/// <summary>The short wait while the request is read, with a way to start right away.</summary>
public sealed class IntakeWaitingTests : TestContext
{
    private readonly FakeOrderService _orders = new();
    private readonly List<OrderSubmission> _started = [];

    public IntakeWaitingTests()
    {
        Services.AddSingleton<IOrderService>(_orders);
        Services.AddScoped<CircuitDataGate>();
    }

    [Fact]
    public void SaysWhatHappensAndOffersToSkip()
    {
        var cut = Render(_orders.Add("alice", OrderTestData.Analyzing()).OrderId);

        Assert.Equal("Reading your request…", cut.Find("h2").TextContent);
        Assert.Contains("This takes a few seconds.", cut.Find("[data-testid='intake-waiting']").TextContent);
        Assert.Equal("Skip and start now", cut.Find("[data-testid='intake-skip']").TextContent.Trim());
    }

    [Fact]
    public void SkipAndStartNowCallsSkipClarification()
    {
        var order = _orders.Add("alice", OrderTestData.Analyzing());
        var cut = Render(order.OrderId);

        cut.Find("[data-testid='intake-skip']").Click();

        Assert.Equal((order.OrderId, "alice"), Assert.Single(_orders.Skipped));
        Assert.Equal(order.OrderId, Assert.Single(_started).OrderId);
    }

    [Fact]
    public void AFailedSkipIsExplainedAndCanBeRepeated()
    {
        _orders.SkipError = new InvalidOperationException("db");
        var cut = Render(_orders.Add("alice", OrderTestData.Analyzing()).OrderId);

        cut.Find("[data-testid='intake-skip']").Click();

        Assert.Contains("Try again in a moment.", cut.Find("[data-testid='intake-skip-error']").TextContent);
        Assert.Empty(_started);
        Assert.False(cut.Find("[data-testid='intake-skip']").HasAttribute("disabled"));
    }

    [Fact]
    public void ExtraActionsAppearNextToSkip()
    {
        var cut = RenderComponent<IntakeWaiting>(p => p
            .Add(c => c.OrderId, Guid.NewGuid())
            .AddChildContent("<button data-testid='extra'>Stop</button>"));

        Assert.NotNull(cut.Find(".intake-waiting-actions [data-testid='extra']"));
    }

    private IRenderedComponent<IntakeWaiting> Render(Guid orderId) =>
        RenderComponent<IntakeWaiting>(p => p
            .Add(c => c.OrderId, orderId)
            .Add(c => c.RequestingUsername, "alice")
            .Add(c => c.OnStarted, EventCallback.Factory.Create<OrderSubmission>(this, s => _started.Add(s))));
}
