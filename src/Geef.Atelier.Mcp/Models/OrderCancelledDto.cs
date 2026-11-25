namespace Geef.Atelier.Mcp.Models;

/// <summary>Answer of <c>cancel_order</c>.</summary>
/// <param name="OrderId">Order id.</param>
/// <param name="State">Display state on the wire after the call, as in <see cref="OrderStatusDto.State"/>.</param>
/// <param name="Changed">True when the call stopped the order or it is still stopping; false when nothing was being written.</param>
/// <param name="Message">What happened, in plain English.</param>
public sealed record OrderCancelledDto(string OrderId, string State, bool Changed, string? Message = null);
