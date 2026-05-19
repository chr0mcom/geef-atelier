namespace Geef.Atelier.Core.Domain.Dashboard;

/// <summary>
/// Lightweight SignalR payload pushed when dashboard-relevant state changes.
/// Clients use this to decide whether to refresh the Press rail or prepend a Day-Book entry.
/// </summary>
public sealed record DashboardEvent(
    DashboardEventKind Kind,
    string? Username,
    DayBookEntry? DayBookEntry,
    PressRun? PressRun);

public enum DashboardEventKind
{
    PressUpdated   = 0,
    DayBookUpdated = 1
}
