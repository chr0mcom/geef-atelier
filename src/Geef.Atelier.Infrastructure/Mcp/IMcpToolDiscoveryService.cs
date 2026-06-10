using Geef.Atelier.Core.Domain.Mcp;
using Geef.Atelier.Core.Domain.Tools;

namespace Geef.Atelier.Infrastructure.Mcp;

/// <summary>Discovers tools advertised by a remote MCP server and maps them to <see cref="ToolDefinition"/> candidates.</summary>
public interface IMcpToolDiscoveryService
{
    /// <summary>
    /// Connects to the MCP server, calls <c>tools/list</c>, and returns the results as
    /// <see cref="ToolDefinition"/> candidates (not yet persisted).
    /// </summary>
    Task<IReadOnlyList<ToolDefinition>> DiscoverAsync(McpServerConfig config, CancellationToken ct = default);
}
