using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

/// <summary>
/// Stubs of the removed older tools (decision log D-091). Clients keep tool lists cached, so the names stay listed for one version;
/// every call answers with an error that names the order tool to call instead. The parameters of the removed tools stay optional and
/// unused, so that a call from a cached list is answered with that error and not with a binding error.
/// </summary>
[McpServerToolType]
public static class RetiredTools
{
    /// <summary>Start of the description of every stub, followed by the replacement and <see cref="DescriptionEnd"/>.</summary>
    public const string DescriptionStart = "Removed: use ";

    /// <summary>End of the description of every stub.</summary>
    public const string DescriptionEnd = " instead. Every call returns an error that names it.";

    /// <summary>The error of <c>submit_request</c> and <c>submit_auto_crew_request</c>.</summary>
    public const string SubmitMessage =
        "This tool was removed. To get a text written, call submit_order with the person's request word for word.";

    /// <summary>The error of <c>get_run_status</c>.</summary>
    public const string StatusMessage =
        "This tool was removed. Call get_order_status with the orderId; list_orders shows the orders.";

    /// <summary>The error of <c>get_run_result</c> and <c>get_run_details</c>.</summary>
    public const string ResultMessage =
        "This tool was removed. Call get_order_result with the orderId; list_orders shows the orders.";

    private const string Unused = "Not used any more.";

    [McpServerTool, Description(DescriptionStart + "submit_order" + DescriptionEnd)]
    public static string SubmitRequest(
        [Description(Unused)] string? briefingText = null,
        [Description(Unused)] string? configJson = null,
        [Description(Unused)] string? crewTemplate = null,
        [Description(Unused)] string? customCrew = null,
        [Description(Unused)] string? attachments = null) =>
        throw new McpException(SubmitMessage);

    [McpServerTool, Description(DescriptionStart + "submit_order" + DescriptionEnd)]
    public static string SubmitAutoCrewRequest(
        [Description(Unused)] string? briefingText = null,
        [Description(Unused)] string? configJson = null,
        [Description(Unused)] bool? chainToTaskRun = null,
        [Description(Unused)] string? attachments = null) =>
        throw new McpException(SubmitMessage);

    [McpServerTool, Description(DescriptionStart + "get_order_status" + DescriptionEnd)]
    public static string GetRunStatus([Description(Unused)] string? runId = null) =>
        throw new McpException(StatusMessage);

    [McpServerTool, Description(DescriptionStart + "get_order_result" + DescriptionEnd)]
    public static string GetRunResult([Description(Unused)] string? runId = null) =>
        throw new McpException(ResultMessage);

    [McpServerTool, Description(DescriptionStart + "get_order_result" + DescriptionEnd)]
    public static string GetRunDetails([Description(Unused)] string? runId = null) =>
        throw new McpException(ResultMessage);
}
