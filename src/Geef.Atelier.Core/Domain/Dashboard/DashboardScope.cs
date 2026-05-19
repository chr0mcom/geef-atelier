namespace Geef.Atelier.Core.Domain.Dashboard;

/// <summary>Controls which runs are included in the dashboard: only the logged-in user's or all users'.</summary>
public enum DashboardScope
{
    My  = 0,
    All = 1
}
