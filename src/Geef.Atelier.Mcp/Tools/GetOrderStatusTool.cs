using System.ComponentModel;
using System.Globalization;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Mcp.Models;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

[McpServerToolType]
public static class GetOrderStatusTool
{
    /// <summary>The wait a call without <c>waitSeconds</c> asks for; never more than <c>Orders:MaxWaitSeconds</c>.</summary>
    public const int DefaultWaitSeconds = 25;

    /// <summary>The description: <c>{0}</c> is the default wait, <c>{1}</c> the longest wait (<c>Orders:MaxWaitSeconds</c>).</summary>
    public const string ToolDescriptionFormat =
        "Reports how an order is doing. Waits up to waitSeconds (default {0}, max {1}) for a change before answering. " +
        "When done is true, call get_order_result. Otherwise call again after nextCheckSeconds; do not build your own sleep loop. " +
        "The answer has the state (preparing, needs_input, queued, working, delivered, delivered_with_open_points, failed, stopped), " +
        "the current step in plain English, the elapsed seconds, the outcome of the current run once it has ended and warnings. " +
        "After revise_order, version is the new version and the state is that of the new version. " +
        "With needs_input it answers at once and repeats the questions: ask the person, then call answer_order.";

    /// <summary>The description with the default of <see cref="OrderOptions.MaxWaitSeconds"/>; the server uses <see cref="Describe"/> with the configured value.</summary>
    public static string ToolDescription { get; } = Describe(new OrderOptions().MaxWaitSeconds);

    /// <summary>The description for the longest wait <paramref name="maxWaitSeconds"/>.</summary>
    public static string Describe(int maxWaitSeconds)
    {
        var max = Math.Max(0, maxWaitSeconds);
        return string.Format(CultureInfo.InvariantCulture, ToolDescriptionFormat, Math.Min(DefaultWaitSeconds, max), max);
    }

    [McpServerTool]
    public static async Task<OrderStatusDto> GetOrderStatus(
        IOrderService orders,
        ICurrentUserService currentUser,
        IOptions<OrderOptions> options,
        [Description("The orderId returned by submit_order.")] string orderId,
        [Description("Seconds to wait for a change before answering. 0 answers at once; the longest wait is named in the description of this tool.")] int waitSeconds = DefaultWaitSeconds,
        CancellationToken cancellationToken = default)
    {
        var id = McpOrderAccess.ParseOrderId(orderId);
        var requestingUsername = currentUser.RequestingUsername();

        var view = await orders.GetAsync(id, requestingUsername, cancellationToken)
            ?? throw new McpException(McpOrderAccess.OrderNotFound);

        var wait = ClampWait(waitSeconds, options.Value.MaxWaitSeconds);
        if (wait > 0 && !view.Display.IsFinished && view.Display.State != OrderDisplayState.NeedsInput)
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
