using Geef.Atelier.Application.Runs;
using Geef.Atelier.Web.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Geef.Atelier.Web.Hubs;

/// <summary>
/// SignalR hub for live run-status updates, signed-in users only. Group names are derived on the server from the
/// caller's identity (<see cref="RunUpdateTopics"/>); a caller can never pick another user's group.
/// </summary>
[Authorize]
public sealed class RunHub(IRunService runService) : Hub
{
    /// <summary>Subscribes the caller to updates of a run they own (any run for administrators).</summary>
    public async Task JoinRunGroupAsync(Guid runId)
    {
        if (!await runService.CanWatchRunAsync(Context.User, runId, Context.ConnectionAborted))
            throw new HubException("This run is not available.");
        await Groups.AddToGroupAsync(Context.ConnectionId, RunUpdateTopics.Run(runId));
    }

    /// <summary>Unsubscribes the caller from updates of a run.</summary>
    public Task LeaveRunGroupAsync(Guid runId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, RunUpdateTopics.Run(runId));

    /// <summary>Subscribes the caller to list updates: own runs, or all runs for administrators.</summary>
    public Task JoinRunsListGroupAsync() =>
        Context.User.RunListTopic() is { } topic
            ? Groups.AddToGroupAsync(Context.ConnectionId, topic)
            : throw new HubException("Sign in required.");

    /// <summary>Unsubscribes the caller from list updates.</summary>
    public Task LeaveRunsListGroupAsync() =>
        Context.User.RunListTopic() is { } topic
            ? Groups.RemoveFromGroupAsync(Context.ConnectionId, topic)
            : Task.CompletedTask;

    /// <summary>Subscribes the caller to dashboard events of their own runs.</summary>
    public Task JoinDashboardGroupAsync() =>
        Context.User.WatcherName() is { } name
            ? Groups.AddToGroupAsync(Context.ConnectionId, RunUpdateTopics.UserDashboard(name))
            : throw new HubException("Sign in required.");

    /// <summary>Unsubscribes the caller from their dashboard events.</summary>
    public Task LeaveDashboardGroupAsync() =>
        Context.User.WatcherName() is { } name
            ? Groups.RemoveFromGroupAsync(Context.ConnectionId, RunUpdateTopics.UserDashboard(name))
            : Task.CompletedTask;

    /// <summary>Subscribes an administrator to dashboard events of all users.</summary>
    public Task JoinDashboardAllGroupAsync() =>
        Context.User.IsAtelierAdmin()
            ? Groups.AddToGroupAsync(Context.ConnectionId, RunUpdateTopics.AdminDashboard)
            : throw new HubException("Administrators only.");

    /// <summary>Unsubscribes the caller from the all-users dashboard events.</summary>
    public Task LeaveDashboardAllGroupAsync() =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, RunUpdateTopics.AdminDashboard);
}
