using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Orders;

namespace Geef.Atelier.Tests.Orders;

public sealed class OrderStateResolverTests
{
    public static TheoryData<int, OrderState, RunStatus?, RunOutcome?, bool, OrderDisplayState, bool> TruthTable => new()
    {
        { 1, OrderState.Cancelled, RunStatus.Running, null, false, OrderDisplayState.Stopped, true },
        { 1, OrderState.Cancelled, null, null, false, OrderDisplayState.Stopped, true },
        { 1, OrderState.Cancelled, RunStatus.Completed, RunOutcome.Converged, true, OrderDisplayState.Stopped, true },
        { 2, OrderState.Analyzing, null, null, false, OrderDisplayState.Preparing, false },
        { 3, OrderState.NeedsInput, null, null, false, OrderDisplayState.NeedsInput, false },
        { 4, OrderState.Ready, null, null, false, OrderDisplayState.Queued, false },
        { 8, OrderState.Ready, RunStatus.Pending, null, false, OrderDisplayState.Queued, false },
        { 9, OrderState.Ready, RunStatus.Running, null, false, OrderDisplayState.Working, false },
        { 10, OrderState.Ready, RunStatus.Aborted, RunOutcome.Aborted, true, OrderDisplayState.Stopped, true },
        { 10, OrderState.Ready, RunStatus.Aborted, RunOutcome.DeliveredWithOpenPoints, true, OrderDisplayState.DeliveredWithOpenPoints, true },
        { 10, OrderState.Ready, RunStatus.Aborted, RunOutcome.DeliveredWithOpenPoints, false, OrderDisplayState.Stopped, true },
        { 11, OrderState.Ready, RunStatus.Failed, RunOutcome.Aborted, true, OrderDisplayState.Stopped, true },
        { 12, OrderState.Ready, RunStatus.Completed, RunOutcome.DeliveredWithOpenPoints, true, OrderDisplayState.DeliveredWithOpenPoints, true },
        { 13, OrderState.Ready, RunStatus.Completed, RunOutcome.Converged, true, OrderDisplayState.Delivered, true },
        { 13, OrderState.Ready, RunStatus.Completed, null, true, OrderDisplayState.Delivered, true },
        { 14, OrderState.Ready, RunStatus.Failed, RunOutcome.Converged, true, OrderDisplayState.Delivered, true },
        { 15, OrderState.Ready, RunStatus.Failed, RunOutcome.DeliveredWithOpenPoints, true, OrderDisplayState.DeliveredWithOpenPoints, true },
        { 15, OrderState.Ready, RunStatus.Failed, RunOutcome.Failed, true, OrderDisplayState.DeliveredWithOpenPoints, true },
        { 15, OrderState.Ready, RunStatus.Failed, null, true, OrderDisplayState.DeliveredWithOpenPoints, true },
        { 16, OrderState.Ready, RunStatus.Completed, RunOutcome.Converged, false, OrderDisplayState.Failed, true },
        { 16, OrderState.Ready, RunStatus.Failed, RunOutcome.Failed, false, OrderDisplayState.Failed, true },
        { 16, OrderState.Ready, RunStatus.Completed, RunOutcome.DeliveredWithOpenPoints, false, OrderDisplayState.Failed, true },
        { 9, OrderState.Analyzing, RunStatus.Running, null, false, OrderDisplayState.Working, false },
    };

    [Theory]
    [MemberData(nameof(TruthTable))]
    public void EveryRowOfTheTruthTableResolves(int row, OrderState state, RunStatus? status, RunOutcome? outcome, bool hasText, OrderDisplayState expected, bool finished)
    {
        var run = status is null ? null : new OrderRunState(status.Value, outcome, hasText);

        var display = OrderStateResolver.Resolve(state, run);

        Assert.True(expected == display.State, $"row {row}: expected {expected}, got {display.State}");
        Assert.Equal(finished, display.IsFinished);
    }

    [Fact]
    public void AnOrderWithTextNeverShowsFailed()
    {
        var checkedCombinations = 0;
        foreach (var state in Enum.GetValues<OrderState>())
        foreach (var status in Enum.GetValues<RunStatus>())
        foreach (var outcome in Enum.GetValues<RunOutcome>().Select(o => (RunOutcome?)o).Append(null))
        {
            var display = OrderStateResolver.Resolve(state, new OrderRunState(status, outcome, HasText: true));
            checkedCombinations++;
            Assert.True(display.HasText);
            Assert.NotEqual(OrderDisplayState.Failed, display.State);
            if (status is RunStatus.Completed or RunStatus.Failed && outcome != RunOutcome.Aborted && state != OrderState.Cancelled)
                Assert.True(display.State is OrderDisplayState.Delivered or OrderDisplayState.DeliveredWithOpenPoints,
                    $"{state}/{status}/{outcome}: {display.State}");
        }

        Assert.Equal(4 * 5 * 5, checkedCombinations);
    }

    [Fact]
    public void SelectCurrentRunPrefersTheHighestVersionThenTheNewest()
    {
        var t = DateTimeOffset.UtcNow;
        var v1 = Run(1, t.AddMinutes(5));
        var v2Old = Run(2, t.AddMinutes(1));
        var v2New = Run(2, t.AddMinutes(2));

        Assert.Same(v2New, OrderStateResolver.SelectCurrentRun([v1, v2Old, v2New]));
        Assert.Null(OrderStateResolver.SelectCurrentRun([]));
    }

    [Fact]
    public void ToRunStateTreatsBlankTextAsNoText()
    {
        var run = Run(1, DateTimeOffset.UtcNow, finalText: "  \n ");
        Assert.False(run.ToRunState(DateTimeOffset.UtcNow).HasText);
        Assert.True(Run(1, DateTimeOffset.UtcNow, finalText: "Hello").ToRunState(DateTimeOffset.UtcNow).HasText);
    }

    [Theory]
    [InlineData(RunStatus.Completed, null, 2, true)]
    [InlineData(RunStatus.Failed, null, 2, true)]
    [InlineData(RunStatus.Aborted, null, 2, true)]
    [InlineData(RunStatus.Completed, null, 31, false)]
    [InlineData(RunStatus.Completed, RunOutcome.Converged, 2, false)]
    [InlineData(RunStatus.Failed, RunOutcome.Failed, 2, false)]
    [InlineData(RunStatus.Running, null, 2, false)]
    public void A_run_is_finishing_for_a_short_while_until_its_outcome_is_stored(RunStatus status, RunOutcome? outcome, int secondsAgo, bool finishing)
    {
        var now = new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
        var run = new RunEntity
        {
            Id = Guid.NewGuid(), CreatedAt = now.AddMinutes(-5), Status = status, BriefingText = "b",
            Outcome = outcome, CompletedAt = status is RunStatus.Running ? null : now.AddSeconds(-secondsAgo), FinalText = "Text",
        };

        var state = run.ToRunState(now);

        Assert.Equal(finishing, state.Finishing);
        if (finishing)
            Assert.Equal(new OrderDisplay(OrderDisplayState.Working, true, false), OrderStateResolver.Resolve(OrderState.Ready, state));
    }

    [Fact]
    public void A_cancelled_order_is_stopped_even_while_its_run_is_finishing()
    {
        var display = OrderStateResolver.Resolve(OrderState.Cancelled, new OrderRunState(RunStatus.Aborted, null, false, Finishing: true));

        Assert.Equal(OrderDisplayState.Stopped, display.State);
    }

    [Theory]
    [InlineData(OrderDisplayState.Preparing, "preparing")]
    [InlineData(OrderDisplayState.NeedsInput, "needs_input")]
    [InlineData(OrderDisplayState.Queued, "queued")]
    [InlineData(OrderDisplayState.Working, "working")]
    [InlineData(OrderDisplayState.Delivered, "delivered")]
    [InlineData(OrderDisplayState.DeliveredWithOpenPoints, "delivered_with_open_points")]
    [InlineData(OrderDisplayState.Failed, "failed")]
    [InlineData(OrderDisplayState.Stopped, "stopped")]
    public void WireNamesAreSnakeCase(OrderDisplayState state, string wire) => Assert.Equal(wire, state.ToWireName());

    private static RunEntity Run(int? version, DateTimeOffset createdAt, string? finalText = null) => new()
    {
        Id = Guid.NewGuid(), CreatedAt = createdAt, Status = RunStatus.Completed, BriefingText = "b",
        OrderVersion = version, FinalText = finalText,
    };
}
