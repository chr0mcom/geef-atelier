using Geef.Atelier.Core.Domain.Dashboard;

namespace Geef.Atelier.Core.Notifications;

/// <summary>Frontend-agnostic abstraction for notifying clients that a run was updated.</summary>
public interface IRunNotifier
{
    /// <summary>Notifies subscribers that the given run was updated. Fire-and-forget safe; idempotent.</summary>
    Task NotifyRunUpdatedAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pushes a <see cref="DashboardEvent"/> to connected dashboard clients, invalidates the
    /// affected user's cache entry, and (for admin-visible events) the All-scope cache.
    /// </summary>
    Task NotifyDashboardEventAsync(DashboardEvent dashboardEvent, CancellationToken cancellationToken = default);
}
