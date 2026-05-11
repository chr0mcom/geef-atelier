namespace Geef.Atelier.Core.Notifications;

/// <summary>Frontend-agnostic abstraction for notifying clients that a run was updated.</summary>
public interface IRunNotifier
{
    /// <summary>Notifies subscribers that the given run was updated. Fire-and-forget safe; idempotent.</summary>
    Task NotifyRunUpdatedAsync(Guid runId, CancellationToken cancellationToken = default);
}
