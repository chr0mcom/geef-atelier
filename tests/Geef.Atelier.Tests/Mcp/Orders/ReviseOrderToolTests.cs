using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Mcp.Models;
using Geef.Atelier.Mcp.Tools;
using ModelContextProtocol;

namespace Geef.Atelier.Tests.Mcp.Orders;

/// <summary><c>revise_order</c> passes the person's words unchanged, refuses foreign orders without telling anything about them and reports refusals in plain English.</summary>
public sealed class ReviseOrderToolTests
{
    private readonly FakeOrderService _orders = new();
    private readonly FakeUser _alice = new("alice");

    [Fact]
    public async Task The_words_of_the_person_and_the_quick_changes_reach_the_service_unchanged()
    {
        var id = Owned("alice");
        const string change = "  Das Treffen ist am 5. Mai, nicht am 12. Mai.\n";
        _orders.OnRevise = r => new OrderRevision(r.OrderId, OrderDisplayState.Queued, Guid.NewGuid(), 2, []);

        var dto = await ReviseOrderTool.ReviseOrder(_orders, _alice, id.ToString(), change, ["Shorter", "more_formal", "shorter", " "]);

        var request = Assert.Single(_orders.Revisions);
        Assert.Equal((id, "alice", change, OrderEntry.Mcp), (request.OrderId, request.RequestingUsername, request.Change, request.Entry));
        Assert.Equal([RevisionQuick.Shorter, RevisionQuick.MoreFormal], request.Quick);
        Assert.Equal((id.ToString(), "queued", 2), (dto.OrderId, dto.State, dto.Version));
        Assert.Empty(dto.Notices);
        Assert.Equal(ReviseOrderTool.StartedMessage, dto.Message);
        Assert.Contains("get_order_status", dto.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Quick_changes_alone_are_enough()
    {
        var id = Owned("alice");

        await ReviseOrderTool.ReviseOrder(_orders, _alice, id.ToString(), quick: ["simpler words"]);

        var request = Assert.Single(_orders.Revisions);
        Assert.Null(request.Change);
        Assert.Equal([RevisionQuick.Simpler], request.Quick);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("other")]
    public async Task A_foreign_or_unknown_order_is_not_found_and_nothing_is_changed(string? owner)
    {
        var id = owner is null ? Guid.NewGuid() : Owned(owner);

        var ex = await Assert.ThrowsAsync<McpException>(() => ReviseOrderTool.ReviseOrder(_orders, _alice, id.ToString(), "Make it shorter."));

        Assert.Equal("Order not found.", ex.Message);
        Assert.Empty(_orders.Revisions);
    }

    [Fact]
    public async Task An_invalid_id_is_reported_like_an_unknown_order()
    {
        var ex = await Assert.ThrowsAsync<McpException>(() => ReviseOrderTool.ReviseOrder(_orders, _alice, "not-an-id", "Make it shorter."));

        Assert.Equal("Order not found.", ex.Message);
    }

    [Fact]
    public async Task A_foreign_order_with_an_unknown_quick_change_says_only_not_found()
    {
        var id = Owned("other");

        var ex = await Assert.ThrowsAsync<McpException>(() => ReviseOrderTool.ReviseOrder(_orders, _alice, id.ToString(), quick: ["shortest"]));

        Assert.Equal("Order not found.", ex.Message);
    }

    [Fact]
    public async Task The_refusal_not_found_of_the_service_reads_like_every_other_unknown_order()
    {
        var id = Owned("alice");
        _orders.ReviseException = new OrderRevisionException(RevisionLines.NotFound);

        var ex = await Assert.ThrowsAsync<McpException>(() => ReviseOrderTool.ReviseOrder(_orders, _alice, id.ToString(), "Make it shorter."));

        Assert.Equal("Order not found.", ex.Message);
    }

    [Fact]
    public async Task An_administrator_changes_without_owner_filter()
    {
        var id = Owned("bob");

        await ReviseOrderTool.ReviseOrder(_orders, new FakeUser("root", isAdmin: true), id.ToString(), "Make it shorter.");

        Assert.Null(Assert.Single(_orders.Revisions).RequestingUsername);
    }

    [Fact]
    public async Task An_unknown_quick_change_names_the_ones_that_work()
    {
        var id = Owned("alice");

        var ex = await Assert.ThrowsAsync<McpException>(() => ReviseOrderTool.ReviseOrder(_orders, _alice, id.ToString(), quick: ["shortest"]));

        Assert.Equal("Unknown quick change \"shortest\". Use shorter, longer, more_formal, friendlier or simpler.", ex.Message);
        Assert.Empty(_orders.Revisions);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Without_words_and_quick_changes_nothing_is_asked_of_the_service(string? change)
    {
        var id = Owned("alice");

        var ex = await Assert.ThrowsAsync<McpException>(() => ReviseOrderTool.ReviseOrder(_orders, _alice, id.ToString(), change, []));

        Assert.Equal(ReviseOrderTool.NoChangeMessage, ex.Message);
        Assert.Empty(_orders.Revisions);
    }

    [Theory]
    [InlineData(RevisionLines.NotAvailable)]
    [InlineData(RevisionLines.Stopped)]
    [InlineData(RevisionLines.NothingToChange)]
    [InlineData("This order already has 10 versions, the most one order can have. Please start a new order for further changes.")]
    public async Task A_refusal_of_the_service_comes_back_in_its_words(string message)
    {
        var id = Owned("alice");
        _orders.ReviseException = new OrderRevisionException(message);

        var ex = await Assert.ThrowsAsync<McpException>(() => ReviseOrderTool.ReviseOrder(_orders, _alice, id.ToString(), "Make it shorter."));

        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public async Task A_second_change_while_a_version_is_written_says_so_and_names_the_running_version()
    {
        var id = Owned("alice");
        _orders.OnRevise = r => new OrderRevision(r.OrderId, OrderDisplayState.Working, Guid.NewGuid(), 2, [RevisionLines.Busy]);

        var dto = await ReviseOrderTool.ReviseOrder(_orders, _alice, id.ToString(), "Make it shorter.");

        Assert.Equal(("working", 2), (dto.State, dto.Version));
        Assert.Equal([RevisionLines.Busy], dto.Notices);
        Assert.Equal(ReviseOrderTool.BusyMessage, dto.Message);
        Assert.Contains("nothing was changed", dto.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("shorter", RevisionQuick.Shorter)]
    [InlineData(" LONGER ", RevisionQuick.Longer)]
    [InlineData("more_formal", RevisionQuick.MoreFormal)]
    [InlineData("More formal", RevisionQuick.MoreFormal)]
    [InlineData("more-formal", RevisionQuick.MoreFormal)]
    [InlineData("friendlier", RevisionQuick.Friendlier)]
    [InlineData("simpler", RevisionQuick.Simpler)]
    [InlineData("Simpler words", RevisionQuick.Simpler)]
    public void Quick_changes_are_read_by_their_wire_and_button_names(string wire, RevisionQuick expected)
    {
        Assert.Equal(expected, OrderWireNames.ParseRevisionQuick(wire));
        Assert.Equal(expected, OrderWireNames.ParseRevisionQuick(expected.ToWire()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("shortest")]
    [InlineData("formal")]
    [InlineData("more__formal_please")]
    public void Unknown_quick_changes_are_not_guessed(string wire) =>
        Assert.Null(OrderWireNames.ParseRevisionQuick(wire));

    [Fact]
    public void Every_quick_button_has_a_wire_name()
    {
        Assert.Equal(Enum.GetValues<RevisionQuick>().Select(q => q.ToWire()), OrderWireNames.QuickNames);
        Assert.All(OrderWireNames.QuickNames, name => Assert.Contains(name, ReviseOrderTool.UnknownQuickFormat, StringComparison.Ordinal));
    }

    private Guid Owned(string owner)
    {
        var id = Guid.NewGuid();
        _orders.Add(owner, OrderToolTestData.View(id, OrderDisplayState.Delivered));
        return id;
    }
}
