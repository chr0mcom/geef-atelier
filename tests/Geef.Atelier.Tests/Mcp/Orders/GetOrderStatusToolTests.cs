using System.Diagnostics;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Mcp.Tools;
using ModelContextProtocol;

namespace Geef.Atelier.Tests.Mcp.Orders;

public sealed class GetOrderStatusToolTests
{
    private readonly FakeOrderService _orders = new();
    private readonly FakeUser _alice = new("alice");

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(25, 25)]
    [InlineData(50, 50)]
    [InlineData(99, 50)]
    public void Wait_is_clamped_to_zero_and_the_configured_maximum(int requested, int expected) =>
        Assert.Equal(expected, GetOrderStatusTool.ClampWait(requested, 50));

    [Theory]
    [InlineData(-5, null)]
    [InlineData(0, null)]
    [InlineData(25, 25)]
    [InlineData(99, 50)]
    public async Task The_tool_waits_for_the_clamped_time_and_not_at_all_for_zero(int requested, int? expectedWait)
    {
        var id = AddWorking();
        _orders.ViewAfterWait = OrderToolTestData.View(id, OrderDisplayState.Working);

        await GetOrderStatusTool.GetOrderStatus(_orders, _alice, OrderToolTestData.Options(), id.ToString(), requested);

        if (expectedWait is null)
            Assert.Empty(_orders.Waits);
        else
            Assert.Equal(TimeSpan.FromSeconds(expectedWait.Value), Assert.Single(_orders.Waits).MaxWait);
    }

    [Fact]
    public async Task The_configured_maximum_wins_over_fifty()
    {
        var id = AddWorking();
        _orders.ViewAfterWait = OrderToolTestData.View(id, OrderDisplayState.Working);

        await GetOrderStatusTool.GetOrderStatus(_orders, _alice, OrderToolTestData.Options(maxWaitSeconds: 30), id.ToString(), 99);

        Assert.Equal(TimeSpan.FromSeconds(30), Assert.Single(_orders.Waits).MaxWait);
    }

    [Fact]
    public async Task Default_wait_is_twenty_five_seconds()
    {
        var id = AddWorking();
        _orders.ViewAfterWait = OrderToolTestData.View(id, OrderDisplayState.Working);

        await GetOrderStatusTool.GetOrderStatus(_orders, _alice, OrderToolTestData.Options(), id.ToString());

        Assert.Equal(TimeSpan.FromSeconds(25), Assert.Single(_orders.Waits).MaxWait);
    }

    [Fact]
    public async Task The_wait_starts_from_the_current_state_of_the_caller_and_returns_the_change()
    {
        var id = Guid.NewGuid();
        var runId = Guid.NewGuid();
        _orders.Add("alice", OrderToolTestData.View(id, OrderDisplayState.Queued, step: "Waiting to start"));
        _orders.ViewAfterWait = OrderToolTestData.View(id, OrderDisplayState.Working, step: "Writing", elapsed: TimeSpan.FromSeconds(41.6), runId: runId, version: 1);

        var dto = await GetOrderStatusTool.GetOrderStatus(_orders, _alice, OrderToolTestData.Options(), id.ToString(), 25);

        var wait = Assert.Single(_orders.Waits);
        Assert.Equal(OrderDisplayState.Queued, wait.KnownState);
        Assert.Equal("alice", wait.RequestingUsername);
        Assert.Equal("working", dto.State);
        Assert.Equal("Writing", dto.Step);
        Assert.Equal(42, dto.ElapsedSeconds);
        Assert.Equal(runId.ToString(), dto.RunId);
        Assert.Equal(1, dto.Version);
        Assert.False(dto.Done);
        Assert.Equal(10, dto.NextCheckSeconds);
        Assert.Null(dto.Outcome);
    }

    [Fact]
    public async Task A_finished_order_answers_at_once_with_done_outcome_and_warnings()
    {
        var id = Guid.NewGuid();
        _orders.Add("alice", OrderToolTestData.View(id, OrderDisplayState.DeliveredWithOpenPoints, step: "Done",
            runId: Guid.NewGuid(), outcome: RunOutcome.DeliveredWithOpenPoints,
            warnings: [OrderToolTestData.Warning("grounding.source_warning", "Page 3 of offer.pdf has no text.")]));

        var dto = await GetOrderStatusTool.GetOrderStatus(_orders, _alice, OrderToolTestData.Options(), id.ToString(), 50);

        Assert.Empty(_orders.Waits);
        Assert.True(dto.Done);
        Assert.Equal(0, dto.NextCheckSeconds);
        Assert.Equal("delivered_with_open_points", dto.State);
        Assert.Equal(nameof(RunOutcome.DeliveredWithOpenPoints), dto.Outcome);
        var warning = Assert.Single(dto.Warnings);
        Assert.Equal("grounding.source_warning", warning.Code);
        Assert.Equal("Page 3 of offer.pdf has no text.", warning.Message);
    }

    [Theory]
    [InlineData(OrderDisplayState.Preparing, "preparing", false)]
    [InlineData(OrderDisplayState.NeedsInput, "needs_input", false)]
    [InlineData(OrderDisplayState.Queued, "queued", false)]
    [InlineData(OrderDisplayState.Working, "working", false)]
    [InlineData(OrderDisplayState.Delivered, "delivered", true)]
    [InlineData(OrderDisplayState.DeliveredWithOpenPoints, "delivered_with_open_points", true)]
    [InlineData(OrderDisplayState.Failed, "failed", true)]
    [InlineData(OrderDisplayState.Stopped, "stopped", true)]
    public async Task Every_state_has_its_wire_name_and_done_flag(OrderDisplayState state, string wire, bool done)
    {
        var id = Guid.NewGuid();
        _orders.Add("alice", OrderToolTestData.View(id, state));

        var dto = await GetOrderStatusTool.GetOrderStatus(_orders, _alice, OrderToolTestData.Options(), id.ToString(), 0);

        Assert.Equal(wire, dto.State);
        Assert.Equal(done, dto.Done);
        Assert.Equal(done ? 0 : 10, dto.NextCheckSeconds);
    }

    [Fact]
    public void Every_display_state_is_covered_by_the_test_above() =>
        Assert.Equal(8, Enum.GetValues<OrderDisplayState>().Length);

    [Fact]
    public async Task A_foreign_order_is_reported_as_not_found_without_waiting()
    {
        var id = Guid.NewGuid();
        _orders.Add("bob", OrderToolTestData.View(id, OrderDisplayState.Working));

        var ex = await Assert.ThrowsAsync<McpException>(() =>
            GetOrderStatusTool.GetOrderStatus(_orders, _alice, OrderToolTestData.Options(), id.ToString(), 25));

        Assert.Equal("Order not found.", ex.Message);
        Assert.Empty(_orders.Waits);
    }

    [Fact]
    public async Task An_administrator_sees_foreign_orders()
    {
        var id = Guid.NewGuid();
        _orders.Add("bob", OrderToolTestData.View(id, OrderDisplayState.Delivered));

        var dto = await GetOrderStatusTool.GetOrderStatus(_orders, new FakeUser("root", isAdmin: true), OrderToolTestData.Options(), id.ToString(), 0);

        Assert.Equal("delivered", dto.State);
        Assert.Null(Assert.Single(_orders.Gets).RequestingUsername);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    public async Task An_invalid_id_is_reported_as_not_found(string orderId)
    {
        var ex = await Assert.ThrowsAsync<McpException>(() =>
            GetOrderStatusTool.GetOrderStatus(_orders, _alice, OrderToolTestData.Options(), orderId, 0));

        Assert.Equal("Order not found.", ex.Message);
    }

    [Fact]
    public async Task An_order_deleted_during_the_wait_is_reported_as_not_found()
    {
        var id = AddWorking();
        _orders.DeleteDuringWait = true;

        var ex = await Assert.ThrowsAsync<McpException>(() =>
            GetOrderStatusTool.GetOrderStatus(_orders, _alice, OrderToolTestData.Options(), id.ToString(), 10));

        Assert.Equal("Order not found.", ex.Message);
    }

    [Fact]
    public async Task A_client_that_goes_away_ends_the_wait_at_once()
    {
        var id = AddWorking();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var watch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            GetOrderStatusTool.GetOrderStatus(_orders, _alice, OrderToolTestData.Options(), id.ToString(), 50, cts.Token));

        watch.Stop();
        Assert.True(_orders.ObservedCancellation);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"The wait took {watch.Elapsed}.");
    }

    [Fact]
    public async Task Without_a_change_the_unchanged_state_is_returned_after_the_wait()
    {
        var id = AddWorking();

        var dto = await GetOrderStatusTool.GetOrderStatus(_orders, _alice, OrderToolTestData.Options(maxWaitSeconds: 1), id.ToString(), 25);

        Assert.Equal("working", dto.State);
        Assert.Equal(TimeSpan.FromSeconds(1), Assert.Single(_orders.Waits).MaxWait);
    }

    private Guid AddWorking()
    {
        var id = Guid.NewGuid();
        _orders.Add("alice", OrderToolTestData.View(id, OrderDisplayState.Working, runId: Guid.NewGuid()));
        return id;
    }
}
