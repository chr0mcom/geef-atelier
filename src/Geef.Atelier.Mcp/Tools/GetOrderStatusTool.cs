using System.ComponentModel;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Mcp.Models;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

[McpServerToolType]
public static class GetOrderStatusTool
{
    public const string ToolDescription =
        "Reports how an order is doing. Waits up to waitSeconds (default 25, max 50) for a change before answering. " +
        "When done is true, call get_order_result. Otherwise call again after nextCheckSeconds; do not build your own sleep loop. " +
        "The answer has the state (queued, working, delivered, delivered_with_open_points, failed, stopped; needs_input is reserved for orders that need answers), " +
        "the current step in plain English, the elapsed seconds, the outcome of the current run once it has ended and warnings.";

    [McpServerTool, Description(ToolDescription)]
    public static async Task<OrderStatusDto> GetOrderStatus(
        IOrderService orders,
        ICurrentUserService currentUser,
        IOptions<OrderOptions> options,
        [Description("The orderId returned by submit_order.")] string orderId,
        [Description("Seconds to wait for a change before answering (0 to 50, default 25). 0 answers at once.")] int waitSeconds = 25,
        CancellationToken cancellationToken = default)
    {
        var id = McpOrderAccess.ParseOrderId(orderId);
        var requestingUsername = currentUser.RequestingUsername();

        var view = await orders.GetAsync(id, requestingUsername, cancellationToken)
            ?? throw new McpException(McpOrderAccess.OrderNotFound);

        var wait = ClampWait(waitSeconds, options.Value.MaxWaitSeconds);
        if (wait > 0 && !view.Display.IsFinished)
        {
            view = await orders.WaitForChangeAsync(id, requestingUsername, view.Display.State, TimeSpan.FromSeconds(wait), cancellationToken)
                ?? throw new McpException(McpOrderAccess.OrderNotFound);
        }

        return view.ToStatusDto();
    }

    /// <summary>Clamps the requested wait to <c>[0, maxWaitSeconds]</c>.</summary>
    public static int ClampWait(int waitSeconds, int maxWaitSeconds) =>
        Math.Clamp(waitSeconds, 0, Math.Max(0, maxWaitSeconds));
}
