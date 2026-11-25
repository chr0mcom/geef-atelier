using System.ComponentModel;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Mcp.Models;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

/// <summary><c>list_orders</c>: the caller's own orders, newest activity first.</summary>
[McpServerToolType]
public static class ListOrdersTool
{
    public const int MaxLimit = 50;

    public const int DefaultLimit = 20;

    public const string ToolDescription =
        "Lists the person's own orders, newest activity first, with orderId, title (the beginning of their request), state, version, updatedAt and wordCount. " +
        "Use the orderId with get_order_result, revise_order, export_order or cancel_order.";

    [McpServerTool(ReadOnly = true), Description(ToolDescription)]
    public static async Task<IReadOnlyList<OrderListItemDto>> ListOrders(
        IOrderService orders,
        ICurrentUserService currentUser,
        [Description("How many orders to list (1 to 50, default 20).")] int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var owner = currentUser.ListOwner(allUsers: false);
        var items = await orders.ListAsync(owner, Math.Clamp(limit, 1, MaxLimit), cancellationToken);
        return items
            .Select(i => new OrderListItemDto(i.OrderId.ToString(), i.Title, i.Display.State.ToWire(), i.Version, i.UpdatedAt, i.WordCount))
            .ToList();
    }
}
