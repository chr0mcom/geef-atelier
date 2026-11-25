namespace Geef.Atelier.Core.Configuration;

/// <summary>Which tools the MCP server offers (section <c>Mcp</c>).</summary>
public sealed class McpOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Mcp";

    /// <summary>Whether the older tools are offered beside the order tools.</summary>
    public bool LegacyTools { get; set; } = true;
}
