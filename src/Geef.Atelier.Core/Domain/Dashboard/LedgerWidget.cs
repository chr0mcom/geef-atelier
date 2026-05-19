namespace Geef.Atelier.Core.Domain.Dashboard;

/// <summary>Cost and run-count summary tiles for the dashboard header.</summary>
public sealed record LedgerStats(
    LedgerTile Today,
    LedgerTile ThisWeek,
    LedgerTile ThisMonth,
    LedgerTile AllTime);

/// <summary>One tile in the ledger: run count + cost + week-on-week trend.</summary>
public sealed record LedgerTile(
    int RunCount,
    decimal CostEur,
    TrendDirection Trend,
    decimal TrendPct);

public enum TrendDirection
{
    Up   = 1,
    Flat = 0,
    Down = -1
}
