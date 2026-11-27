using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

/// <summary>
/// Stub of the removed template studio, like <see cref="AnalyzeTemplateProposalTool"/>: listed while
/// <see cref="Core.Configuration.McpOptions.LegacyTools"/> is true, every call answers with an error that names the replacement.
/// </summary>
[McpServerToolType]
public static class MaterializeTemplateProposalTool
{
    [McpServerTool, Description(McpToolCatalog.LegacyLeadStart + "submit_order" + McpToolCatalog.LegacyLeadEnd +
        "Removed: the template studio no longer exists, and every call returns an error.")]
    public static string MaterializeTemplateProposal(
        [Description("Not used any more.")] string? analysisId = null) =>
        throw new McpException(AnalyzeTemplateProposalTool.RemovedMessage);
}
