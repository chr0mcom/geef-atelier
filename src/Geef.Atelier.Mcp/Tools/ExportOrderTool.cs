using System.ComponentModel;
using System.Globalization;
using System.Text;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Mcp.Models;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

/// <summary><c>export_order</c>: the text of a version as the file the web app downloads (Markdown, Word or PDF).</summary>
[McpServerToolType]
public static class ExportOrderTool
{
    public const string ToolDescription =
        "Returns the text of an order as a file: md (Markdown, also returned as text), docx (Word) or pdf, the same file the Atelier web app downloads. " +
        "Without version it is the version get_order_result shows; pass version for another one. " +
        "The file holds only the text, with its placeholders in square brackets: still tell the person what was checked and the open points from get_order_result.";

    public const string UnknownFormatFormat = "Unknown format \"{0}\". Use md, docx or pdf.";

    public const string VersionMessage = "version must be 1 or higher, or left out for the version get_order_result shows.";

    public const string NoTextMessage = "This order has no text to download yet.";

    public const string NoVersionTextFormat = "Version {0} of this order has no text to download.";

    [McpServerTool(ReadOnly = true), Description(ToolDescription)]
    public static async Task<OrderExportDto> ExportOrder(
        IOrderService orders,
        IOrderExportService exports,
        ICurrentUserService currentUser,
        [Description("The orderId from submit_order or list_orders.")] string orderId,
        [Description("md (Markdown, default), docx (Word) or pdf.")] string format = "md",
        [Description("Optional version (1, 2, …). Leave it out for the version get_order_result shows.")] int? version = null,
        CancellationToken cancellationToken = default)
    {
        var id = McpOrderAccess.ParseOrderId(orderId);
        var requestingUsername = currentUser.RequestingUsername();
        _ = await orders.GetAsync(id, requestingUsername, cancellationToken)
            ?? throw new McpException(McpOrderAccess.OrderNotFound);

        if (!ExportFormatExtensions.TryParseRoute(format, out var exportFormat))
            throw new McpException(string.Format(CultureInfo.InvariantCulture, UnknownFormatFormat, Shorten(format?.Trim() ?? string.Empty)));
        if (version is < 1)
            throw new McpException(VersionMessage);

        var export = await exports.ExportAsync(id, exportFormat, version, requestingUsername, cancellationToken)
                     ?? throw new McpException(version is { } wanted
                         ? string.Format(CultureInfo.InvariantCulture, NoVersionTextFormat, wanted)
                         : NoTextMessage);
        return new OrderExportDto(
            export.FileName,
            export.ContentType,
            Convert.ToBase64String(export.Content),
            exportFormat == ExportFormat.Markdown ? Encoding.UTF8.GetString(export.Content) : null);
    }

    private static string Shorten(string value) => value.Length <= 20 ? value : value[..20] + "…";
}
