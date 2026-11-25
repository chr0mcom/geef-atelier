using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Web.Components;
using Geef.Atelier.Web.Components.Pages;
using Geef.Atelier.Web.Notifications;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Geef.Atelier.Tests.Web.Components.Orders;

/// <summary>"My work": orders instead of runs, with search, filter, pages, delete with confirmation and live updates.</summary>
public sealed class MyWorkTests : TestContext
{
    private readonly FakeOrderService _orders = new();
    private readonly ScriptedOrderFeed _feed = new();
    private readonly TestAuthorizationContext _auth;

    public MyWorkTests()
    {
        Services.AddSingleton<IOrderService>(_orders);
        Services.AddSingleton<IRunUpdateFeed>(_feed);
        Services.AddScoped<CircuitDataGate>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        _auth = this.AddTestAuthorization();
        _auth.SetAuthorized("alice");
    }

    [Fact]
    public void IsReachableAsWorkAndAsOrders()
    {
        var routes = typeof(MyWork).GetCustomAttributes<RouteAttribute>().Select(r => r.Template).ToList();

        Assert.Contains("/work", routes);
        Assert.Contains("/orders", routes);
    }

    [Fact]
    public void TheEmptyListOffersToWriteSomething()
    {
        var cut = RenderComponent<MyWork>();

        Assert.Contains("Nothing here yet.", cut.Find("[data-testid='work-empty']").TextContent);
        Assert.Equal("/start", cut.Find("[data-testid='work-empty-start']").GetAttribute("href"));
    }

    [Fact]
    public void ListsOwnOrdersNewestFirstWithStateDateWordsAndFiles()
    {
        _orders.Add("alice", OrderTestData.View(OrderDisplayState.Delivered, title: "Older letter", updatedAt: OrderTestData.Start.AddHours(1), wordCount: 1_250,
            sources: [OrderTestData.Source(), OrderTestData.Source("b.md")]));
        _orders.Add("alice", OrderTestData.View(OrderDisplayState.Working, title: "Newer summary", updatedAt: OrderTestData.Start.AddHours(2), mode: OrderMode.Careful));
        _orders.Add("bob", OrderTestData.View(OrderDisplayState.Delivered, title: "Bob's letter"));

        var cut = RenderComponent<MyWork>();

        var rows = cut.FindAll("[data-testid='work-row']");
        Assert.Equal(2, rows.Count);
        Assert.Equal("Newer summary", rows[0].QuerySelector("[data-testid='work-row-link']")!.TextContent);
        Assert.Equal("working", rows[0].QuerySelector("[data-testid='order-state']")!.GetAttribute("data-state"));
        Assert.Contains("Extra careful", rows[0].TextContent);
        Assert.Contains("1,250 words", rows[1].TextContent);
        Assert.Contains("2 files", rows[1].TextContent);
        Assert.StartsWith("/orders/", rows[1].QuerySelector("[data-testid='work-row-link']")!.GetAttribute("href"));
        Assert.DoesNotContain("Bob", cut.Markup);
        Assert.Equal("alice", _orders.ListedFor.First());
    }

    [Fact]
    public void FiltersAndSearchNarrowTheList()
    {
        _orders.Add("alice", OrderTestData.View(OrderDisplayState.Delivered, title: "Letter to the bank"));
        _orders.Add("alice", OrderTestData.View(OrderDisplayState.DeliveredWithOpenPoints, title: "Offer for a client"));
        _orders.Add("alice", OrderTestData.View(OrderDisplayState.Working, title: "Summary of the bill"));
        _orders.Add("alice", OrderTestData.View(OrderDisplayState.Failed, title: "Speech"));
        var cut = RenderComponent<MyWork>();

        var pills = cut.FindAll("button.filter-pill").Select(b => b.TextContent.Trim()).ToList();
        Assert.Equal(new[] { "All (4)", "In progress (1)", "Ready (2)", "Stopped or failed (1)" }, pills);

        cut.FindAll("button.filter-pill")[2].Click();
        Assert.Equal(2, cut.FindAll("[data-testid='work-row']").Count);

        cut.Find("[data-testid='work-search']").Input("bank");
        Assert.Equal("Letter to the bank", cut.Find("[data-testid='work-row-link']").TextContent);

        cut.Find("[data-testid='work-search']").Input("nothing like this");
        Assert.NotNull(cut.Find("[data-testid='work-no-match']"));
        cut.Find("[data-testid='work-clear']").Click();
        Assert.Equal(4, cut.FindAll("[data-testid='work-row']").Count);
    }

    [Fact]
    public void LongListsArePaged()
    {
        for (var i = 0; i < 45; i++)
            _orders.Add("alice", OrderTestData.View(OrderDisplayState.Delivered, title: $"Order {i:00}", updatedAt: OrderTestData.Start.AddMinutes(i)));
        var cut = RenderComponent<MyWork>();

        Assert.Equal(20, cut.FindAll("[data-testid='work-row']").Count);
        Assert.Equal("Page 1 of 3", cut.Find("[data-testid='work-page-info']").TextContent);
        Assert.Equal("Order 44", cut.Find("[data-testid='work-row-link']").TextContent);
        Assert.True(cut.Find("[data-testid='work-prev']").HasAttribute("disabled"));

        cut.Find("[data-testid='work-next']").Click();
        cut.Find("[data-testid='work-next']").Click();

        Assert.Equal(5, cut.FindAll("[data-testid='work-row']").Count);
        Assert.Equal("Page 3 of 3", cut.Find("[data-testid='work-page-info']").TextContent);
        Assert.True(cut.Find("[data-testid='work-next']").HasAttribute("disabled"));
    }

    [Fact]
    public void OnlyFinishedOrdersCanBeDeletedAndOnlyAfterConfirming()
    {
        var done = _orders.Add("alice", OrderTestData.View(OrderDisplayState.Delivered, title: "Done", updatedAt: OrderTestData.Start.AddHours(2)));
        _orders.Add("alice", OrderTestData.View(OrderDisplayState.Working, title: "Busy", updatedAt: OrderTestData.Start.AddHours(1)));
        var cut = RenderComponent<MyWork>();

        var rows = cut.FindAll("[data-testid='work-row']");
        Assert.NotNull(rows[0].QuerySelector("[data-testid='work-row-delete']"));
        Assert.Null(rows[1].QuerySelector("[data-testid='work-row-delete']"));

        rows[0].QuerySelector("[data-testid='work-row-delete']")!.Click();
        Assert.Contains("“Done” will be deleted", cut.Find("[data-testid='work-delete-confirm']").TextContent);
        cut.Find("[data-testid='work-delete-cancel']").Click();
        Assert.Empty(_orders.Deleted);

        cut.Find("[data-testid='work-row-delete']").Click();
        cut.Find("[data-testid='work-delete-confirm-button']").Click();

        Assert.Equal(done.OrderId, Assert.Single(_orders.Deleted));
        Assert.Equal("alice", Assert.Single(_orders.DeletedFor));
        Assert.Single(cut.FindAll("[data-testid='work-row']"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASignInWithoutUserNameShowsNoListAndAsksToSignInAgain(bool withEmptyName)
    {
        _orders.Add("bob", OrderTestData.View(OrderDisplayState.Delivered, title: "Bob's private letter"));
        if (withEmptyName)
            _auth.SetAuthorized(string.Empty);
        else
            this.SignInWithoutUserName();

        var cut = RenderComponent<MyWork>();

        Assert.Equal(OwnerScope.UnknownUserMessage, cut.Find("[data-testid='work-unknown-user']").TextContent.Trim());
        Assert.DoesNotContain("private letter", cut.Markup);
        Assert.Empty(_orders.ListedFor);
        Assert.Empty(cut.FindAll("[data-testid='work-empty']"));
    }

    [Fact]
    public void ARefusedDeleteIsExplained()
    {
        _orders.DeleteResult = false;
        _orders.Add("alice", OrderTestData.View(OrderDisplayState.Delivered));
        var cut = RenderComponent<MyWork>();

        cut.Find("[data-testid='work-row-delete']").Click();
        cut.Find("[data-testid='work-delete-confirm-button']").Click();

        Assert.Contains("could not be deleted", cut.Find("[data-testid='work-action-error']").TextContent);
    }

    [Fact]
    public void DeletingAnOrderThatIsStillWorkingSaysToStopItFirst()
    {
        _orders.DeleteError = new OrderStillWorkingException();
        _orders.Add("alice", OrderTestData.View(OrderDisplayState.Delivered));
        var cut = RenderComponent<MyWork>();

        cut.Find("[data-testid='work-row-delete']").Click();
        cut.Find("[data-testid='work-delete-confirm-button']").Click();

        Assert.Contains("This order is still being worked on. Stop it first.", cut.Find("[data-testid='work-action-error']").TextContent);
        Assert.DoesNotContain("Something went wrong", cut.Markup);
    }

    [Fact]
    public void AdministratorsCanShowAllUsers()
    {
        _auth.SetAuthorized("root");
        _auth.SetRoles("admin");
        _orders.Add("bob", OrderTestData.View(OrderDisplayState.Delivered, title: "Bob's letter"));
        var cut = RenderComponent<MyWork>();
        Assert.Empty(cut.FindAll("[data-testid='work-row']"));

        cut.Find("[data-testid='work-all-users']").Change(true);

        Assert.Equal("Bob's letter", cut.Find("[data-testid='work-row-link']").TextContent);
        Assert.Null(_orders.ListedFor.Last());
    }

    [Fact]
    public void NormalUsersHaveNoAllUsersSwitch()
    {
        var cut = RenderComponent<MyWork>();

        Assert.Empty(cut.FindAll("[data-testid='work-all-users']"));
    }

    [Fact]
    public async Task ALiveUpdateRefreshesTheList()
    {
        var cut = RenderComponent<MyWork>();
        Assert.NotNull(_feed.ListHandler);

        var view = _orders.Add("alice", OrderTestData.View(OrderDisplayState.Queued, title: "Fresh order"));
        await _feed.ListHandler!(view.OrderId);

        cut.WaitForAssertion(() => Assert.Equal("Fresh order", cut.Find("[data-testid='work-row-link']").TextContent));
    }

    [Fact]
    public void ClosingThePageEndsTheSubscription()
    {
        RenderComponent<MyWork>();

        DisposeComponents();

        Assert.Equal(1, _feed.ListSubscriptions);
        Assert.Equal(1, _feed.ListDisposals);
    }

    [Fact]
    public void ALoadErrorOffersToTryAgain()
    {
        _orders.Add("alice", OrderTestData.View(OrderDisplayState.Delivered, title: "Letter"));
        var failing = new FailingListService(_orders);
        Services.AddSingleton<IOrderService>(failing);
        var cut = RenderComponent<MyWork>();

        Assert.Contains("could not be loaded", cut.Find("[data-testid='work-load-failed']").TextContent);

        failing.Fail = false;
        cut.Find("[data-testid='work-reload']").Click();

        Assert.Equal("Letter", cut.Find("[data-testid='work-row-link']").TextContent);
    }

    private sealed class FailingListService(FakeOrderService inner) : IOrderService
    {
        public bool Fail { get; set; } = true;

        public Task<IReadOnlyList<OrderListItem>> ListAsync(string? ownerUsername, int limit = 50, CancellationToken ct = default) =>
            Fail ? throw new InvalidOperationException("db") : inner.ListAsync(ownerUsername, limit, ct);

        public Task<OrderSubmission> SubmitAsync(SubmitOrderRequest request, CancellationToken ct = default) => inner.SubmitAsync(request, ct);
        public Task<SourceSelectionCheck> CheckSourcesAsync(IReadOnlyList<OrderSourceSelection> sources, string ownerUsername, CancellationToken ct = default) => inner.CheckSourcesAsync(sources, ownerUsername, ct);
        public Task<OrderView?> GetAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default) => inner.GetAsync(orderId, requestingUsername, ct);
        public Task<OrderResult?> GetResultAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default) => inner.GetResultAsync(orderId, requestingUsername, ct);
        public Task<OrderView?> WaitForChangeAsync(Guid orderId, string? requestingUsername, OrderDisplayState? knownState, TimeSpan maxWait, CancellationToken ct = default) => inner.WaitForChangeAsync(orderId, requestingUsername, knownState, maxWait, ct);
        public Task<bool> CancelAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default) => inner.CancelAsync(orderId, requestingUsername, ct);
        public Task<bool> DeleteAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default) => inner.DeleteAsync(orderId, requestingUsername, ct);

        public Task<OrderSubmission> AnswerAsync(AnswerOrderRequest request, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<OrderSubmission> SkipClarificationAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }
}
