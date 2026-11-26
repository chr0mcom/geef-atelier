using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Mcp.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp;

/// <summary>
/// The tools the server offers, registered one by one: the order tools always, the ticket tools only while
/// <see cref="UploadOptions.Tickets"/> is <c>On</c>, the older tools only while <see cref="McpOptions.LegacyTools"/> is true.
/// Every tool type of this assembly is in exactly one of the three lists.
/// </summary>
public static class McpToolCatalog
{
    /// <summary>The order tools of the stage-5 contract, in the order of a typical conversation.</summary>
    public static IReadOnlyList<Type> OrderTools { get; } =
    [
        typeof(SubmitOrderTool), typeof(AnswerOrderTool), typeof(GetOrderStatusTool), typeof(GetOrderResultTool),
        typeof(ReviseOrderTool), typeof(ExportOrderTool), typeof(CancelOrderTool), typeof(ListOrdersTool), typeof(ProfileTool),
    ];

    /// <summary>The tools of the upload ticket, offered beside the order tools while <c>Uploads:Tickets</c> is <c>On</c>.</summary>
    public static IReadOnlyList<Type> TicketTools { get; } = [typeof(PrepareAttachmentTool)];

    /// <summary>The order tools; with <paramref name="tickets"/> (<c>Uploads:Tickets=On</c>) also <see cref="TicketTools"/>.</summary>
    public static IReadOnlyList<Type> OrderToolsFor(bool tickets) =>
        tickets ? [.. OrderTools, .. TicketTools] : OrderTools;

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

    /// <summary>Replaces the description of <paramref name="property"/> in the input schema of <paramref name="tool"/>; an unknown property is left alone.</summary>
    public static McpServerTool WithInputPropertyDescription(this McpServerTool tool, string property, string description)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var schema = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!.AsObject();
        if (schema["properties"] is JsonObject properties && properties[property] is JsonObject entry)
            entry["description"] = description;
        tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(schema, McpJsonUtilities.DefaultOptions);
        return tool;
    }

    /// <summary>
    /// Leaves <paramref name="property"/> out of the input schema of <paramref name="tool"/>, so clients are not offered it. The SDK
    /// still binds an argument of that name to its parameter; the tool decides what such a call means.
    /// </summary>
    public static McpServerTool WithoutInputProperty(this McpServerTool tool, string property)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var schema = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!.AsObject();
        if (schema["properties"] is JsonObject properties)
            properties.Remove(property);
        if (schema["required"] is JsonArray required && required.FirstOrDefault(r => r?.GetValue<string>() == property) is { } entry)
            required.Remove(entry);
        tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(schema, McpJsonUtilities.DefaultOptions);
        return tool;
    }
}
