using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

/// <summary>
/// Stub of the removed template studio: the tool name stays listed while <see cref="Core.Configuration.McpOptions.LegacyTools"/>
/// is true, because clients keep tool lists cached, but every call answers with an error that names the replacement.
/// </summary>
[McpServerToolType]
public static class AnalyzeTemplateProposalTool
{
    /// <summary>The error every call of a removed template-studio tool returns.</summary>
    public const string RemovedMessage =
        "This tool was removed together with the template studio. To get a text written, call submit_order with the request.";

    [McpServerTool, Description(McpToolCatalog.LegacyLeadStart + "submit_order" + McpToolCatalog.LegacyLeadEnd +
        "Removed: the template studio no longer exists, and every call returns an error.")]
    public static string AnalyzeTemplateProposal(
        [Description("Not used any more.")] string? taskDescription = null) =>
        throw new McpException(RemovedMessage);
}
