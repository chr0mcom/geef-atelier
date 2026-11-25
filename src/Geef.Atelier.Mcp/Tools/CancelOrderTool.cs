using System.ComponentModel;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Mcp.Models;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

/// <summary><c>cancel_order</c>: stops an order that is being prepared or written.</summary>
[McpServerToolType]
public static class CancelOrderTool
{
    public const string ToolDescription =
        "Stops an order that is being prepared or written, also a new version after revise_order. Call it only when the person asks to stop. " +
        "A text that was already finished stays available with get_order_result and export_order. " +
        "changed is false when nothing was being written.";

    public const string StoppingMessage =
        "The order is being stopped. A text that was already finished stays available with get_order_result.";

    public const string NothingToStopMessage = "Nothing was stopped: the order is not being prepared or written right now.";

    [McpServerTool, Description(ToolDescription)]
    public static async Task<OrderCancelledDto> CancelOrder(
        IOrderService orders,
        ICurrentUserService currentUser,
        [Description("The orderId from submit_order or list_orders.")] string orderId,
        CancellationToken cancellationToken = default)
    {
        var id = McpOrderAccess.ParseOrderId(orderId);
        var requestingUsername = currentUser.RequestingUsername();
        _ = await orders.GetAsync(id, requestingUsername, cancellationToken)
            ?? throw new McpException(McpOrderAccess.OrderNotFound);

        var changed = await orders.CancelAsync(id, requestingUsername, cancellationToken);
        var view = await orders.GetAsync(id, requestingUsername, cancellationToken)
                   ?? throw new McpException(McpOrderAccess.OrderNotFound);
        return new OrderCancelledDto(id.ToString(), view.Display.State.ToWire(), changed, changed ? StoppingMessage : NothingToStopMessage);
    }
}
