namespace Geef.Atelier.Core.Notifications;

/// <summary>Notifies live subscribers that an order changed (state change, new run). Fire-and-forget safe.</summary>
public interface IOrderNotifier
{
    /// <summary>Publishes an update of order <paramref name="orderId"/>.</summary>
    Task NotifyOrderUpdatedAsync(Guid orderId, CancellationToken ct = default);
}
