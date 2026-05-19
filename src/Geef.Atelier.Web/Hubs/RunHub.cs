using Microsoft.AspNetCore.SignalR;

namespace Geef.Atelier.Web.Hubs;

/// <summary>SignalR hub for live run-status updates. Group-based routing keyed by run id, plus a global group for the runs-list page.</summary>
public sealed class RunHub : Hub
{
    /// <summary>Name of the SignalR group subscribed to by the runs-list page for any-run updates.</summary>
    public const string AllRunsGroup = "all-runs";

    /// <summary>Subscribes the caller to live updates for a specific run.</summary>
    public Task JoinRunGroupAsync(Guid runId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupName(runId));

    /// <summary>Unsubscribes the caller from live updates for a specific run.</summary>
    public Task LeaveRunGroupAsync(Guid runId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(runId));

    /// <summary>Subscribes the caller to any-run updates (used by the runs-list page).</summary>
    public Task JoinAllRunsGroupAsync() =>
        Groups.AddToGroupAsync(Context.ConnectionId, AllRunsGroup);

    /// <summary>Unsubscribes the caller from any-run updates.</summary>
    public Task LeaveAllRunsGroupAsync() =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, AllRunsGroup);

    /// <summary>Subscribes the caller to live dashboard updates for their own runs.</summary>
    public Task JoinDashboardGroupAsync(string username) =>
        Groups.AddToGroupAsync(Context.ConnectionId, DashboardGroup(username));

    /// <summary>Unsubscribes the caller from personal dashboard updates.</summary>
    public Task LeaveDashboardGroupAsync(string username) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, DashboardGroup(username));

    /// <summary>Subscribes the caller to the admin all-users dashboard group.</summary>
    public Task JoinDashboardAllGroupAsync() =>
        Groups.AddToGroupAsync(Context.ConnectionId, DashboardAllGroup);

    /// <summary>Unsubscribes the caller from the admin all-users dashboard group.</summary>
    public Task LeaveDashboardAllGroupAsync() =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, DashboardAllGroup);

    /// <summary>Name of the SignalR group for admin all-scope dashboard live updates.</summary>
    public const string DashboardAllGroup = "dashboard-all";

    /// <summary>Returns the SignalR group name for a given run id.</summary>
    internal static string GroupName(Guid runId) => $"run-{runId}";

    /// <summary>Returns the SignalR group name for a user's personal dashboard.</summary>
    internal static string DashboardGroup(string username) => $"dashboard-{username}";
}
