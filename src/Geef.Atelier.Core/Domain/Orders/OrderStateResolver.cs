using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Reports;

namespace Geef.Atelier.Core.Domain.Orders;

/// <summary>
/// The parts of a run that decide how its order is shown. <see cref="Finishing"/> is true for a few seconds after the run
/// reached its final status while its outcome, text or report are still being stored (or, for a composition, while the
/// writing run is being created). <see cref="CarriesBlockingPoint"/> is true for a change of an earlier version whose report keeps
/// a blocking passage of that version open while the fact check blocks (<see cref="FactCheckMode.On"/>), or whose report says that the
/// version it changed needed a check that was not taken over (<see cref="RunReport.EarlierNeedsCheck"/>, in every mode of the fact check).
/// </summary>
public sealed record OrderRunState(RunStatus Status, RunOutcome? Outcome, RunKind Kind, bool HasText, bool Finishing = false, bool CarriesBlockingPoint = false);

/// <summary>How an order is shown: its display state, whether a text exists and whether the order is finished.</summary>
public sealed record OrderDisplay(OrderDisplayState State, bool HasText, bool IsFinished);

/// <summary>
/// Derives the display state of an order purely from <see cref="OrderEntity.State"/> and its current run.
/// A finished run with a text that was not stopped is never shown as <see cref="OrderDisplayState.Failed"/>.
/// </summary>
public static class OrderStateResolver
{
    /// <summary>How long after its final status a run without outcome still counts as finishing.</summary>
    public static readonly TimeSpan FinishingWindow = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Resolves the display state; the first matching row of the truth table wins, and an existing run wins over the order state.
    /// A run that would be shown as <see cref="OrderDisplayState.Delivered"/> but carries a blocking passage of an earlier version
    /// (<see cref="OrderRunState.CarriesBlockingPoint"/>) is shown as <see cref="OrderDisplayState.DeliveredWithOpenPoints"/>, as if it
    /// had found that passage itself.
    /// </summary>
    public static OrderDisplay Resolve(OrderState state, OrderRunState? currentRun)
    {
        var display = ResolveRun(state, currentRun);
        return display.State == OrderDisplayState.Delivered && currentRun?.CarriesBlockingPoint == true
            ? display with { State = OrderDisplayState.DeliveredWithOpenPoints }
            : display;
    }

    private static OrderDisplay ResolveRun(OrderState state, OrderRunState? currentRun)
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

        if (currentRun.Finishing)
            return new(OrderDisplayState.Working, hasText, false);

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
            (RunStatus.Aborted, RunOutcome.DeliveredWithOpenPoints, true) => new(OrderDisplayState.DeliveredWithOpenPoints, true, true),
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

    /// <summary>
    /// Picks the version that is shown: the standard run with the highest <see cref="RunEntity.OrderVersion"/> that has a
    /// <see cref="RunEntity.FinalText"/>; <c>null</c> when no run has a text. While a newer version is written, or after it could not
    /// be finished, the version before stays shown.
    /// </summary>
    public static RunEntity? SelectShownRun(IReadOnlyList<RunEntity> runsOfOrder)
    {
        ArgumentNullException.ThrowIfNull(runsOfOrder);

        return runsOfOrder
            .Where(r => r.Kind == RunKind.Standard && !string.IsNullOrWhiteSpace(r.FinalText))
            .OrderByDescending(r => r.OrderVersion ?? 0)
            .ThenByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .FirstOrDefault();
    }

    /// <summary>
    /// Extracts the parts of <paramref name="run"/> that the resolver needs; a text exists when <see cref="RunEntity.FinalText"/>
    /// is not blank. A run that ended less than <see cref="FinishingWindow"/> before <paramref name="now"/> is still finishing
    /// while it has no outcome, and so is a completed composition (its writing run follows). A run with a text that changes an earlier
    /// version (<see cref="RunEntity.RevisionHint"/> not blank) carries a blocking point when its stored report keeps a blocking passage of
    /// that version (<see cref="FactFinding.FromVersion"/>) open while the fact check ran in <see cref="FactCheckMode.On"/>, or says that the
    /// version it changed was shown as "Ready – please check" and its checks were not taken over (<see cref="RunReport.EarlierNeedsCheck"/>,
    /// whatever the mode of the fact check, because nothing of it was checked again); a report without these fields or one that cannot be
    /// read carries none.
    /// </summary>
    public static OrderRunState ToRunState(this RunEntity run, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(run);
        var ended = run.Status is RunStatus.Completed or RunStatus.Failed or RunStatus.Aborted;
        var recent = run.CompletedAt is { } completed && now - completed < FinishingWindow;
        var finishing = ended && recent
            && (run.Outcome is null || (run.Kind == RunKind.CrewComposition && run.Status == RunStatus.Completed && run.Outcome == RunOutcome.Converged));
        var hasText = !string.IsNullOrWhiteSpace(run.FinalText);
        var carries = hasText && ended && !string.IsNullOrWhiteSpace(run.RevisionHint) && CarriesBlockingPoint(run.ReportJson);
        return new(run.Status, run.Outcome, run.Kind, hasText, finishing, carries);
    }

    private static bool CarriesBlockingPoint(string? reportJson) =>
        RunReportJson.TryParse(reportJson) is { } report
        && (report.EarlierNeedsCheck
            || report.FactCheck is { Mode: FactCheckMode.On } check && (check.Open ?? []).Any(f => f is { Blocking: true, FromVersion: not null }));

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
