using System.Text;
using System.Text.Json;
using Geef.Atelier.Core.Domain.Mcp;
using Geef.Atelier.Core.Domain.Tools;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace Geef.Atelier.Infrastructure.Mcp;

internal sealed class McpToolDiscoveryService(
    IAtelierMcpClientFactory clientFactory,
    ILogger<McpToolDiscoveryService> logger) : IMcpToolDiscoveryService
{
    public async Task<IReadOnlyList<ToolDefinition>> DiscoverAsync(McpServerConfig config, CancellationToken ct = default)
    {
        await using var client = await clientFactory.ConnectAsync(config, ct);
        var tools = await client.ListToolsAsync(cancellationToken: ct);

        logger.LogInformation(
            "McpToolDiscovery: server={Server} discovered {Count} tool(s)",
            config.Name, tools.Count);

        return tools
            .Select(t => MapToDefinition(t, config))
            .ToList();
    }

    private static ToolDefinition MapToDefinition(McpClientTool t, McpServerConfig config)
    {
        var sanitizedName = SanitizeName(t.Name, config.Id);
        var schemaJson = JsonSerializer.Serialize(t.JsonSchema);

        return new ToolDefinition(
            Name:        sanitizedName,
            DisplayName: string.IsNullOrWhiteSpace(t.Title) ? t.Name : t.Title,
            Description: t.Description ?? $"MCP tool '{t.Name}' from server '{config.Name}'.",
            ToolType:    ToolType.McpTool,
            Settings:    new Dictionary<string, string>
            {
                [ToolDefinitionSettingsKeys.McpServerId]     = config.Id.ToString(),
                [ToolDefinitionSettingsKeys.McpOriginalName] = t.Name,
            },
            SecretRef:   null,
            LlmSchema:   JsonSerializer.Deserialize<JsonElement>(schemaJson),
            AccessClass: ToolAccessClass.ReadOnly,
            IsSystem:    false);
    }

    /// <summary>
    /// Converts an MCP tool name to a lowercase kebab-case slug safe for use as a
    /// <see cref="ToolDefinition.Name"/>. Keeps only <c>[a-z0-9-]</c>, trims leading/trailing
    /// hyphens, and truncates to 64 characters.
    /// </summary>
    internal static string SanitizeName(string mcpName, Guid serverId)
    {
        var raw = mcpName
            .ToLowerInvariant()
            .Replace('_', '-')
            .Replace(' ', '-');

        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')
                sb.Append(c);
        }

        var name = sb.ToString().Trim('-');

        if (name.Length < 2)
            name = $"mcp-{serverId.ToString("N")[..8]}-tool";

        // Prefix with "mcp-" if it starts with a digit or hyphen after sanitization
        if (!char.IsLetter(name[0]))
            name = "mcp-" + name;

        // Truncate to 64 chars (ToolDefinition name limit), no trailing hyphen
        if (name.Length > 64)
            name = name[..64].TrimEnd('-');

        return name;
    }
}
