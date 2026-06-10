namespace Geef.Atelier.Core.Domain.Mcp;

/// <summary>
/// Configuration for a remote MCP server that Atelier connects to as a client.
/// </summary>
public sealed record McpServerConfig
{
    /// <summary>Unique identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Human-readable label (e.g. "Internal Toolbox").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Base URL of the MCP server endpoint (SSE or Streamable HTTP).</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>
    /// ENV-Var key whose value is injected as the HTTP Authorization header value
    /// (e.g. "MCP_TOOLBOX_TOKEN" → value used as "Bearer {value}").
    /// <c>null</c> means no auth header is added.
    /// </summary>
    public string? AuthHeaderEnv { get; init; }

    /// <summary>Whether this server is enabled for discovery and tool execution.</summary>
    public bool IsActive { get; init; } = true;

    /// <summary>UTC timestamp when this config was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}
