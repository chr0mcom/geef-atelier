using Geef.Atelier.Core.Domain.Dashboard;
using Geef.Atelier.Core.Notifications;

namespace Geef.Atelier.Tests.Web.Notifications;

/// <summary>Test double for <see cref="IRunNotifier"/> that does nothing.</summary>
internal sealed class NoOpRunNotifier : IRunNotifier
{
    public Task NotifyRunUpdatedAsync(Guid runId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task NotifyDashboardEventAsync(DashboardEvent dashboardEvent, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
