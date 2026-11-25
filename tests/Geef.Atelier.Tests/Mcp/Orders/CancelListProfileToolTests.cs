using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Profiles;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Mcp.Tools;
using Geef.Atelier.Tests.Web.Components.Profile;
using ModelContextProtocol;

namespace Geef.Atelier.Tests.Mcp.Orders;

/// <summary><c>cancel_order</c>, <c>list_orders</c> and <c>profile</c>: only the caller's own orders and details, nothing written to the profile.</summary>
public sealed class CancelListProfileToolTests
{
    private readonly FakeOrderService _orders = new();
    private readonly FakeUser _alice = new("alice");

    [Fact]
    public async Task Cancel_stops_an_own_order_and_reports_the_state_afterwards()
    {
        var id = Guid.NewGuid();
        _orders.Add("alice", OrderToolTestData.View(id, OrderDisplayState.Working, runId: Guid.NewGuid(), version: 2));
        _orders.OnCancel = _ => true;
        _orders.ViewAfterCancel = OrderToolTestData.View(id, OrderDisplayState.Stopped);

        var dto = await CancelOrderTool.CancelOrder(_orders, _alice, id.ToString());

        Assert.Equal((id.ToString(), "stopped", true), (dto.OrderId, dto.State, dto.Changed));
        Assert.Equal(CancelOrderTool.StoppingMessage, dto.Message);
        Assert.Equal((id, "alice"), Assert.Single(_orders.Cancels));
    }

    [Fact]
    public async Task Cancel_of_a_finished_order_changes_nothing_and_says_so()
    {
        var id = Guid.NewGuid();
        _orders.Add("alice", OrderToolTestData.View(id, OrderDisplayState.Delivered));

        var dto = await CancelOrderTool.CancelOrder(_orders, _alice, id.ToString());

        Assert.Equal(("delivered", false), (dto.State, dto.Changed));
        Assert.Equal(CancelOrderTool.NothingToStopMessage, dto.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("other")]
    public async Task Cancel_of_a_foreign_or_unknown_order_is_not_found_and_stops_nothing(string? owner)
    {
        var id = Guid.NewGuid();
        if (owner is not null)
            _orders.Add(owner, OrderToolTestData.View(id, OrderDisplayState.Working, runId: Guid.NewGuid()));
        _orders.OnCancel = _ => true;

        var ex = await Assert.ThrowsAsync<McpException>(() => CancelOrderTool.CancelOrder(_orders, _alice, id.ToString()));

        Assert.Equal("Order not found.", ex.Message);
        Assert.Empty(_orders.Cancels);
    }

    [Fact]
    public async Task List_shows_only_own_orders_also_for_an_administrator()
    {
        var updated = new DateTimeOffset(2026, 11, 20, 9, 30, 0, TimeSpan.Zero);
        _orders.ListItems.Add(("alice", Item("Einladung zum Elternabend", OrderDisplayState.Delivered, 2, 180, updated)));
        _orders.ListItems.Add(("bob", Item("Fremder Auftrag", OrderDisplayState.Working, 1, null, updated)));
        _orders.ListItems.Add(("root", Item("Eigener Auftrag des Admins", OrderDisplayState.Failed, 1, null, updated)));

        var own = await ListOrdersTool.ListOrders(_orders, _alice);
        var admin = await ListOrdersTool.ListOrders(_orders, new FakeUser("root", isAdmin: true));

        var row = Assert.Single(own);
        Assert.Equal(("Einladung zum Elternabend", "delivered", 2, 180, updated), (row.Title, row.State, row.Version, row.WordCount, row.UpdatedAt));
        Assert.Equal("Eigener Auftrag des Admins", Assert.Single(admin).Title);
        Assert.Equal([("alice", ListOrdersTool.DefaultLimit), ("root", ListOrdersTool.DefaultLimit)], _orders.Lists);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(7, 7)]
    [InlineData(50, 50)]
    [InlineData(500, 50)]
    public async Task The_limit_stays_between_one_and_fifty(int asked, int used)
    {
        await ListOrdersTool.ListOrders(_orders, _alice, asked);

        Assert.Equal(used, Assert.Single(_orders.Lists).Limit);
    }

    [Fact]
    public async Task List_without_user_name_is_refused()
    {
        var ex = await Assert.ThrowsAsync<McpException>(() => ListOrdersTool.ListOrders(_orders, new FakeUser(null)));

        Assert.Equal(Geef.Atelier.Application.Auth.OwnerScope.UnknownUserMessage, ex.Message);
        Assert.Empty(_orders.Lists);
    }

    [Fact]
    public async Task Profile_returns_the_own_about_me_details_and_never_writes()
    {
        var profiles = new FakeProfileService();
        profiles.Set("alice", new UserProfileInput("Anna Beispiel", "Elternbeirat", "Grundschule Beispielstadt", Formality.Informal, "de",
            "Viele Grüße, Anna", "Keine Emojis", "Ich schreibe meist an Eltern."));
        profiles.Set("bob", new UserProfileInput("Bob", null, null, Formality.Formal, "en", null, null, null));

        var dto = await ProfileTool.Profile(profiles, _alice);

        Assert.Equal(("Anna Beispiel", "Elternbeirat", "Grundschule Beispielstadt", "informal", "de"), (dto.Name, dto.Role, dto.Organization, dto.Formality, dto.Language));
        Assert.Equal(("Viele Grüße, Anna", "Keine Emojis", "Ich schreibe meist an Eltern."), (dto.Signature, dto.NeverDo, dto.Notes));
        Assert.False(dto.IsEmpty);
        Assert.NotNull(dto.UpdatedAt);
        Assert.Contains("About me", dto.Note, StringComparison.Ordinal);
        Assert.Equal(["alice"], profiles.Loaded);
        Assert.Empty(profiles.Saved);
        Assert.Empty(profiles.Cleared);
    }

    [Fact]
    public async Task An_administrator_reads_only_the_own_profile()
    {
        var profiles = new FakeProfileService();
        profiles.Set("alice", new UserProfileInput("Anna", null, null, Formality.Formal, null, null, null, null));

        var dto = await ProfileTool.Profile(profiles, new FakeUser("root", isAdmin: true));

        Assert.True(dto.IsEmpty);
        Assert.Null(dto.Name);
        Assert.Null(dto.Formality);
        Assert.Equal(["root"], profiles.Loaded);
    }

    [Fact]
    public async Task Profile_without_user_name_is_refused()
    {
        var profiles = new FakeProfileService();

        var ex = await Assert.ThrowsAsync<McpException>(() => ProfileTool.Profile(profiles, new FakeUser(null, isAdmin: true)));

        Assert.Equal(Geef.Atelier.Application.Auth.OwnerScope.UnknownUserMessage, ex.Message);
        Assert.Empty(profiles.Loaded);
    }

    private static OrderListItem Item(string title, OrderDisplayState state, int version, int? words, DateTimeOffset updated) =>
        new(Guid.NewGuid(), title, new OrderDisplay(state, words is not null, OrderToolTestData.IsFinished(state)), OrderMode.Direct,
            updated.AddHours(-1), updated, 0, words, version);
}
