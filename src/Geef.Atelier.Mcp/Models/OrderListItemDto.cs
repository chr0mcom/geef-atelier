namespace Geef.Atelier.Mcp.Models;

/// <summary>One order of <c>list_orders</c>.</summary>
/// <param name="OrderId">Order id, for the other order tools.</param>
/// <param name="Title">The beginning of the person's request.</param>
/// <param name="State">Display state on the wire, as in <see cref="OrderStatusDto.State"/>; that of the newest version.</param>
/// <param name="Version">The version <c>get_order_result</c> shows (the newest with a text); without a text the newest version.</param>
/// <param name="UpdatedAt">When the order last changed.</param>
/// <param name="WordCount">Words of the shown text; <c>null</c> without a text.</param>
public sealed record OrderListItemDto(string OrderId, string Title, string State, int? Version, DateTimeOffset UpdatedAt, int? WordCount);
