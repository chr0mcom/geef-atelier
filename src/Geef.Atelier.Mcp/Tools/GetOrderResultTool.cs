using System.ComponentModel;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Mcp.Models;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

[McpServerToolType]
public static class GetOrderResultTool
{
    public const string ToolDescription =
        "Returns the finished text of an order, the placeholders the person still has to fill in, checks the text did not pass, " +
        "the files that were used and warnings. Also returns the text when some checks are open (state delivered_with_open_points): " +
        "then give the person the text together with the open checks. While the order is still working, text is empty; " +
        "when no text was written (state failed), say so and do not write a replacement yourself.";

    [McpServerTool, Description(ToolDescription)]
    public static async Task<OrderResultDto> GetOrderResult(
        IOrderService orders,
        ICurrentUserService currentUser,
        [Description("The orderId returned by submit_order.")] string orderId,
        CancellationToken cancellationToken = default)
    {
        var id = McpOrderAccess.ParseOrderId(orderId);
        var result = await orders.GetResultAsync(id, currentUser.RequestingUsername(), cancellationToken)
            ?? throw new McpException(McpOrderAccess.OrderNotFound);
        return result.ToDto();
    }
}
