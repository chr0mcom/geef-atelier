using System.ComponentModel;
using System.Globalization;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Mcp.Models;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

/// <summary><c>revise_order</c>: writes a new version of a finished text with only the change the person asks for.</summary>
[McpServerToolType]
public static class ReviseOrderTool
{
    public const string ToolDescription =
        "Changes the text of a finished order and writes it as a new version. Only what the person asks for is changed; everything else stays as it is. " +
        "Pass the person's words about the change in change, word for word as they said them: do not rephrase, shorten, translate or add to them, " +
        "and add no changes of your own. For a simple wish you can pass quick instead of change or together with it. " +
        "Atelier asks no questions and does not look anything up again; the new version is checked like every version. " +
        "Then call get_order_status with waitSeconds, and get_order_result when done is true. Until the new version is ready, get_order_result shows the earlier one. " +
        "Earlier versions stay available with export_order. If the change is refused, give the person the answer and do not change the text yourself.";

    public const string NoChangeMessage = "Say what should change: pass the person's words in change, or a quick change in quick.";

    public const string UnknownQuickFormat = "Unknown quick change \"{0}\". Use shorter, longer, more_formal, friendlier or simpler.";

    public const string StartedMessage =
        "Writing the new version has started. Call get_order_status with waitSeconds, then get_order_result when done is true. " +
        "Until then get_order_result shows the earlier version.";

    public const string BusyMessage =
        "A version is already being written, so nothing was changed. Call get_order_status with waitSeconds, then get_order_result when done is true; " +
        "ask for a further change after that.";

    [McpServerTool, Description(ToolDescription)]
    public static async Task<OrderRevisedDto> ReviseOrder(
        IOrderService orders,
        ICurrentUserService currentUser,
        [Description("The orderId from submit_order or list_orders.")] string orderId,
        [Description("What should change, in the person's own words, word for word as they said them. Do not rephrase, shorten, translate or add to them.")] string? change = null,
        [Description("Optional quick changes, several allowed: shorter, longer, more_formal, friendlier, simpler (simpler words and shorter sentences).")] IReadOnlyList<string>? quick = null,
        CancellationToken cancellationToken = default)
    {
        var id = McpOrderAccess.ParseOrderId(orderId);
        var requestingUsername = currentUser.RequestingUsername();
        _ = await orders.GetAsync(id, requestingUsername, cancellationToken)
            ?? throw new McpException(McpOrderAccess.OrderNotFound);

        var quickChanges = ParseQuick(quick);
        if (string.IsNullOrWhiteSpace(change) && quickChanges.Count == 0)
            throw new McpException(NoChangeMessage);

        OrderRevision revision;
        try
        {
            revision = await orders.ReviseAsync(new ReviseOrderRequest(id, requestingUsername, change, quickChanges, OrderEntry.Mcp), cancellationToken);
        }
        catch (OrderRevisionException ex) when (ex.Message == RevisionLines.NotFound)
        {
            throw new McpException(McpOrderAccess.OrderNotFound, ex);
        }
        catch (OrderRevisionException ex)
        {
            throw new McpException(ex.Message, ex);
        }

        return new OrderRevisedDto(
            revision.OrderId.ToString(),
            revision.State.ToWire(),
            revision.Version,
            revision.Notices,
            revision.Notices.Contains(RevisionLines.Busy, StringComparer.Ordinal) ? BusyMessage : StartedMessage);
    }

    /// <summary>The quick changes of the wire, each once, in the order given; an unknown name is refused with the names that work.</summary>
    public static IReadOnlyList<RevisionQuick> ParseQuick(IReadOnlyList<string>? quick)
    {
        var parsed = new List<RevisionQuick>();
        foreach (var name in quick ?? [])
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;
            var value = OrderWireNames.ParseRevisionQuick(name)
                        ?? throw new McpException(string.Format(CultureInfo.InvariantCulture, UnknownQuickFormat, Shorten(name.Trim())));
            if (!parsed.Contains(value))
                parsed.Add(value);
        }
        return parsed;
    }

    private static string Shorten(string value) => value.Length <= 30 ? value : value[..30] + "…";
}
