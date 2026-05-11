using Geef.Atelier.Core.Notifications;

namespace Geef.Atelier.Tests.Web.Notifications;

/// <summary>Test double for <see cref="IRunNotifier"/> that does nothing.</summary>
internal sealed class NoOpRunNotifier : IRunNotifier
{
    /// <inheritdoc/>
    public Task NotifyRunUpdatedAsync(Guid runId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
