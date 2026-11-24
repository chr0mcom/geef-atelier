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
    public static OrderDisplay Resolve(OrderState state, OrderRunState? currentRun) =>
        throw new NotImplementedException();

    /// <summary>
    /// Picks the current run of an order: among standard runs the highest <see cref="RunEntity.OrderVersion"/>, on a tie
    /// the newest; without a standard run the newest run of any kind; <c>null</c> for no runs.
    /// </summary>
    public static RunEntity? SelectCurrentRun(IReadOnlyList<RunEntity> runsOfOrder) =>
        throw new NotImplementedException();

    /// <summary>Extracts the parts of <paramref name="run"/> that the resolver needs; a text exists when <see cref="RunEntity.FinalText"/> is not blank.</summary>
    public static OrderRunState ToRunState(this RunEntity run) =>
        throw new NotImplementedException();

    /// <summary>Returns the snake_case name used on the wire (MCP, JSON), e.g. <c>delivered_with_open_points</c>.</summary>
    public static string ToWireName(this OrderDisplayState state) =>
        throw new NotImplementedException();
}
