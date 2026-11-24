using System.Security.Claims;
using Geef.Atelier.Core.Domain.Dashboard;

namespace Geef.Atelier.Web.Notifications;

/// <summary>
/// In-process live updates for server-rendered components. Subscriptions are scoped to what the signed-in user may
/// see; the topic is derived on the server, never chosen by the caller.
/// </summary>
public interface IRunUpdateFeed
{
    /// <summary>
    /// Subscribes to updates of run <paramref name="runId"/>. Returns null (and subscribes nothing) when
    /// <paramref name="user"/> may not see the run or when <paramref name="ct"/> is cancelled before the access check
    /// has finished, so a component disposed during the check leaves no subscription behind.
    /// </summary>
    Task<IDisposable?> WatchRunAsync(ClaimsPrincipal user, Guid runId, Func<Guid, Task> onUpdated, CancellationToken ct = default);

    /// <summary>
    /// Subscribes to list updates: every run for administrators, the user's own runs otherwise. Anonymous users get
    /// an inert subscription.
    /// </summary>
    IDisposable WatchRunList(ClaimsPrincipal user, Func<Guid, Task> onUpdated);

    /// <summary>
    /// Subscribes to dashboard events of the user's own runs, or of all users when <paramref name="allUsers"/> is set
    /// and the user is an administrator (non-admins silently get their own events only).
    /// </summary>
    IDisposable WatchDashboard(ClaimsPrincipal user, bool allUsers, Func<DashboardEvent, Task> onEvent);

    /// <summary>
    /// Queues a run update for the run topic, the owner's list topic and the admin list topic; returns without waiting
    /// for subscribers.
    /// </summary>
    Task PublishRunUpdatedAsync(Guid runId, string? owner);

    /// <summary>Queues a dashboard event for the owner's and the admin dashboard topic; returns without waiting.</summary>
    Task PublishDashboardEventAsync(DashboardEvent dashboardEvent);
}
