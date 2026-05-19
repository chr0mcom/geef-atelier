using Geef.Atelier.Application.Dashboard;
using Geef.Atelier.Core.Domain.Dashboard;
using Geef.Atelier.Core.Notifications;
using Geef.Atelier.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Geef.Atelier.Web.Notifications;

/// <summary>Sends run-update and dashboard notifications to SignalR clients via <see cref="RunHub"/>.</summary>
internal sealed class SignalRRunNotifier(
    IHubContext<RunHub> hub,
    IDashboardService dashboardService) : IRunNotifier
{
    /// <inheritdoc/>
    public async Task NotifyRunUpdatedAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        try { await hub.Clients.Group(RunHub.GroupName(runId)).SendAsync("RunUpdated", runId, cancellationToken); } catch { }
        try { await hub.Clients.Group(RunHub.AllRunsGroup).SendAsync("AnyRunUpdated", runId, cancellationToken); } catch { }
    }

    /// <inheritdoc/>
    public async Task NotifyDashboardEventAsync(DashboardEvent dashboardEvent, CancellationToken cancellationToken = default)
    {
        // Invalidate cache for the affected user and all-scope snapshots
        if (dashboardEvent.Username is { } u)
            dashboardService.InvalidateUser(u);
        dashboardService.InvalidateAdmin();

        // Push to personal dashboard group
        if (dashboardEvent.Username is { } username)
        {
            try
            {
                await hub.Clients.Group(RunHub.DashboardGroup(username))
                    .SendAsync("DashboardUpdated", dashboardEvent, cancellationToken);
            }
            catch { }
        }

        // Push to all-scope group (admins watching all)
        try
        {
            await hub.Clients.Group(RunHub.DashboardAllGroup)
                .SendAsync("DashboardUpdated", dashboardEvent, cancellationToken);
        }
        catch { }
    }
}
