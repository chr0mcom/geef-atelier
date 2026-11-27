namespace Geef.Atelier.Core.Configuration;

/// <summary>Which tools and extensions the MCP server offers (section <c>Mcp</c>).</summary>
public sealed class McpOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Mcp";

    /// <summary>Whether the older tools are offered beside the order tools.</summary>
    public bool LegacyTools { get; set; } = true;

    /// <summary>Whether the server offers the skill about using Atelier (extension <c>io.modelcontextprotocol/skills</c>).</summary>
    public McpSkillsMode Skills { get; set; } = McpSkillsMode.Off;
}

/// <summary>Whether the MCP server offers its skill (<see cref="McpOptions.Skills"/>).</summary>
public enum McpSkillsMode
{
    /// <summary>No skill, no skills extension, no resources: the server offers exactly what it offered before.</summary>
    Off = 0,

    /// <summary>The server offers the skill about using Atelier to clients that support skills.</summary>
    On = 1,
}
