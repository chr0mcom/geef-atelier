namespace Geef.Atelier.Core.Domain.Orders;

/// <summary>State of an order before its first run exists. Stored as text; once a run exists, the run decides what is shown.</summary>
public enum OrderState { Analyzing = 0, NeedsInput = 1, Ready = 2, Cancelled = 3 }

/// <summary>
/// The stored mode of an order. Every new order is <see cref="Direct"/> (one writing pass plus checks). <see cref="Careful"/> only stands
/// in orders stored by earlier versions; whatever is still done with such an order runs on the direct path.
/// </summary>
public enum OrderMode { Direct = 0, Careful = 1 }

/// <summary>Where an order came from.</summary>
public enum OrderEntry { Web = 0, Mcp = 1, Legacy = 2 }

/// <summary>State of an order as the person sees it, derived from the order state and its current run.</summary>
public enum OrderDisplayState { Preparing = 0, NeedsInput = 1, Queued = 2, Working = 3, Delivered = 4, DeliveredWithOpenPoints = 5, Failed = 6, Stopped = 7 }
