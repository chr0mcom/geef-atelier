using System.Reflection;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Mcp.Tools;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp;

/// <summary>
/// The tools the server offers, registered one by one: the order tools always, the older tools only while
/// <see cref="McpOptions.LegacyTools"/> is true. Every tool type of this assembly is in exactly one of both lists.
/// </summary>
public static class McpToolCatalog
{
    /// <summary>The order tools of the stage-5 contract, in the order of a typical conversation.</summary>
    public static IReadOnlyList<Type> OrderTools { get; } =
    [
        typeof(SubmitOrderTool), typeof(AnswerOrderTool), typeof(GetOrderStatusTool), typeof(GetOrderResultTool),
        typeof(ReviseOrderTool), typeof(ExportOrderTool), typeof(CancelOrderTool), typeof(ListOrdersTool), typeof(ProfileTool),
    ];

    /// <summary>The older tools, kept beside the order tools while <see cref="McpOptions.LegacyTools"/> is true.</summary>
    public static IReadOnlyList<Type> LegacyTools { get; } =
    [
        typeof(AnalyzeTemplateProposalTool), typeof(CancelRunTool), typeof(DownloadRunArtifactTool), typeof(GetProviderModelsTool),
        typeof(GetRunDetailsTool), typeof(GetRunResultTool), typeof(GetRunStatusTool), typeof(ListAdvisorProfilesTool),
        typeof(ListCrewTemplatesTool), typeof(ListGroundingProviderProfilesTool), typeof(ListKnowledgeDocumentsTool),
        typeof(ListLearningsTool), typeof(ListProvidersTool), typeof(ListReviewerProfilesTool), typeof(ListRunArtifactsTool),
        typeof(ListRunsTool), typeof(MaterializeTemplateProposalTool), typeof(SubmitAutoCrewRequestTool), typeof(SubmitRequestTool),
    ];

    /// <summary>Start of the description of an older tool that has a successor, followed by the successor and <see cref="LegacyLeadEnd"/>.</summary>
    public const string LegacyLeadStart = "Older tool, kept for now: use ";

    /// <summary>End of the first sentence of the description of an older tool that has a successor.</summary>
    public const string LegacyLeadEnd = " instead. ";

    /// <summary>The tool methods of <paramref name="toolTypes"/>, as <c>WithTools</c> would find them.</summary>
    public static IReadOnlyList<MethodInfo> ToolMethods(this IEnumerable<Type> toolTypes) =>
        toolTypes
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .ToList();

    /// <summary>Creates the tool of <paramref name="method"/> with the services of the host; <paramref name="description"/> replaces the one of the attribute.</summary>
    public static McpServerTool CreateTool(this MethodInfo method, IServiceProvider services, string? description = null) =>
        McpServerTool.Create(method, target: null, new McpServerToolCreateOptions { Services = services, Description = description });
}
