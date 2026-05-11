using Geef.Atelier.Core.Notifications;
using Geef.Atelier.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Geef.Atelier.Web.Notifications;

/// <summary>Sends run-update notifications to SignalR clients via <see cref="RunHub"/>.</summary>
internal sealed class SignalRRunNotifier(IHubContext<RunHub> hub) : IRunNotifier
{
    /// <inheritdoc/>
    public async Task NotifyRunUpdatedAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        try { await hub.Clients.Group(RunHub.GroupName(runId)).SendAsync("RunUpdated", runId, cancellationToken); } catch { }
        try { await hub.Clients.Group(RunHub.AllRunsGroup).SendAsync("AnyRunUpdated", runId, cancellationToken); } catch { }
    }
}
