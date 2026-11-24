namespace Geef.Atelier.Core.Domain.Orders;

/// <summary>The parts of a run that decide how its order is shown.</summary>
public sealed record OrderRunState(RunStatus Status, RunOutcome? Outcome, RunKind Kind, bool HasText);

/// <summary>How an order is shown: its display state, whether a text exists and whether the order is finished.</summary>
public sealed record OrderDisplay(OrderDisplayState State, bool HasText, bool IsFinished);

/// <summary>
/// Derives the display state of an order purely from <see cref="OrderEntity.State"/> and its current run.
/// A finished run with a text that was not stopped is never shown as <see cref="OrderDisplayState.Failed"/>.
/// </summary>
public static class OrderStateResolver
{
    /// <summary>Resolves the display state; the first matching row of the truth table wins, and an existing run wins over the order state.</summary>
    public static OrderDisplay Resolve(OrderState state, OrderRunState? currentRun)
    {
        var hasText = currentRun?.HasText ?? false;

        if (state == OrderState.Cancelled)
            return new(OrderDisplayState.Stopped, hasText, true);

        if (currentRun is null)
            return state switch
            {
                OrderState.Analyzing => new(OrderDisplayState.Preparing, false, false),
                OrderState.NeedsInput => new(OrderDisplayState.NeedsInput, false, false),
                _ => new(OrderDisplayState.Queued, false, false),
            };

        if (currentRun.Kind == RunKind.CrewComposition)
            return currentRun.Status switch
            {
                RunStatus.Pending or RunStatus.Running => new(OrderDisplayState.Working, hasText, false),
                RunStatus.Aborted => new(OrderDisplayState.Stopped, hasText, true),
                _ => new(OrderDisplayState.Failed, hasText, true),
            };

        return (currentRun.Status, currentRun.Outcome, hasText) switch
        {
            (RunStatus.Pending, _, _) => new(OrderDisplayState.Queued, hasText, false),
            (RunStatus.Running, _, _) => new(OrderDisplayState.Working, hasText, false),
            (RunStatus.Aborted, _, _) => new(OrderDisplayState.Stopped, hasText, true),
            (RunStatus.Failed, RunOutcome.Aborted, _) => new(OrderDisplayState.Stopped, hasText, true),
            (RunStatus.Completed, RunOutcome.DeliveredWithOpenPoints, true) => new(OrderDisplayState.DeliveredWithOpenPoints, true, true),
            (RunStatus.Completed, _, true) => new(OrderDisplayState.Delivered, true, true),
            (RunStatus.Failed, RunOutcome.Converged, true) => new(OrderDisplayState.Delivered, true, true),
            (RunStatus.Failed, _, true) => new(OrderDisplayState.DeliveredWithOpenPoints, true, true),
            _ => new(OrderDisplayState.Failed, false, true),
        };
    }

    /// <summary>
    /// Picks the current run of an order: among standard runs the highest <see cref="RunEntity.OrderVersion"/>, on a tie
    /// the newest; without a standard run the newest run of any kind; <c>null</c> for no runs.
    /// </summary>
    public static RunEntity? SelectCurrentRun(IReadOnlyList<RunEntity> runsOfOrder)
    {
        ArgumentNullException.ThrowIfNull(runsOfOrder);

        var standard = runsOfOrder
            .Where(r => r.Kind == RunKind.Standard)
            .OrderByDescending(r => r.OrderVersion ?? 0)
            .ThenByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .FirstOrDefault();

        return standard ?? runsOfOrder
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .FirstOrDefault();
    }

    /// <summary>Extracts the parts of <paramref name="run"/> that the resolver needs; a text exists when <see cref="RunEntity.FinalText"/> is not blank.</summary>
    public static OrderRunState ToRunState(this RunEntity run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new(run.Status, run.Outcome, run.Kind, !string.IsNullOrWhiteSpace(run.FinalText));
    }

    /// <summary>Returns the snake_case name used on the wire (MCP, JSON), e.g. <c>delivered_with_open_points</c>.</summary>
    public static string ToWireName(this OrderDisplayState state) => state switch
    {
        OrderDisplayState.Preparing => "preparing",
        OrderDisplayState.NeedsInput => "needs_input",
        OrderDisplayState.Queued => "queued",
        OrderDisplayState.Working => "working",
        OrderDisplayState.Delivered => "delivered",
        OrderDisplayState.DeliveredWithOpenPoints => "delivered_with_open_points",
        OrderDisplayState.Failed => "failed",
        OrderDisplayState.Stopped => "stopped",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };
}
