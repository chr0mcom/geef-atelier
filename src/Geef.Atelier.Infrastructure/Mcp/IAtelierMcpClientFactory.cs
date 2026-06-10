using Geef.Atelier.Core.Domain.Mcp;
using ModelContextProtocol.Client;

namespace Geef.Atelier.Infrastructure.Mcp;

/// <summary>
/// Creates authenticated, SSRF-checked <see cref="McpClient"/> connections to configured MCP servers.
/// Callers are responsible for disposing the returned client.
/// </summary>
public interface IAtelierMcpClientFactory
{
    /// <summary>
    /// Connects to the MCP server described by <paramref name="config"/>.
    /// Throws <see cref="InvalidOperationException"/> when the URL fails SSRF validation.
    /// </summary>
    Task<McpClient> ConnectAsync(McpServerConfig config, CancellationToken ct = default);
}
