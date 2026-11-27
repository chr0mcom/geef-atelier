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

    /// <summary>Whether <c>submit_order</c> may show its questions to the person as a form of the client.</summary>
    public McpElicitationMode Elicitation { get; set; } = McpElicitationMode.Off;

    /// <summary>How long the answers of a form can come back before the order is answered the usual way.</summary>
    public TimeSpan ElicitationLifetime { get; set; } = TimeSpan.FromMinutes(30);
}

/// <summary>Whether the MCP server offers its skill (<see cref="McpOptions.Skills"/>).</summary>
public enum McpSkillsMode
{
    /// <summary>No skill, no skills extension, no resources: the server offers exactly what it offered before.</summary>
    Off = 0,

    /// <summary>The server offers the skill about using Atelier to clients that support skills.</summary>
    On = 1,
}

/// <summary>Whether <c>submit_order</c> asks its questions as a form of the client (<see cref="McpOptions.Elicitation"/>).</summary>
public enum McpElicitationMode
{
    /// <summary>Questions always come back as <c>needs_input</c>, as before.</summary>
    Off = 0,

    /// <summary>A client that supports forms on protocol revision 2026-07-28 shows the questions as a form; every other client gets <c>needs_input</c>.</summary>
    On = 1,
}
