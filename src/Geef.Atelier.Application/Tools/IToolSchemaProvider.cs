using Geef.Atelier.Core.Domain.Tools;

namespace Geef.Atelier.Application.Tools;

/// <summary>
/// Produces the <see cref="ToolSchema"/> descriptor for a <see cref="ToolDefinition"/>.
/// The returned schema is used to populate the tools list in an LLM request so the model
/// knows how to invoke each tool.
/// </summary>
public interface IToolSchemaProvider
{
    /// <summary>
    /// Returns a <see cref="ToolSchema"/> for the given <paramref name="tool"/>.
    /// When <see cref="ToolDefinition.LlmSchema"/> is a non-empty JSON object it takes
    /// precedence over the built-in type defaults (custom schema override).
    /// </summary>
    ToolSchema GetSchema(ToolDefinition tool);
}
