namespace Geef.Atelier.Core.Persistence.Orders;

/// <summary>Raised by <see cref="IOrderRepository"/> when a linked source was deleted before the order was stored; nothing was stored.</summary>
public sealed class OrderSourceMissingException(Exception inner) : InvalidOperationException("A source linked to the order no longer exists.", inner);
