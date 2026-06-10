namespace Geef.Atelier.Application.Tools;

/// <summary>
/// Describes the tool descriptor that is sent to an LLM in its tools list.
/// This is the Application-layer equivalent of the Infrastructure-level <c>LlmTool</c> record,
/// defined here so that Application code does not depend on Infrastructure.
/// </summary>
/// <param name="Name">Unique tool name (kebab-case).</param>
/// <param name="Description">Short prose description passed verbatim to the LLM.</param>
/// <param name="InputSchemaJson">
/// JSON Schema string describing the input object the LLM must supply.
/// Must be a valid JSON object schema (e.g. <c>{"type":"object","properties":{...}}</c>).
/// </param>
public sealed record ToolSchema(
    string Name,
    string Description,
    string InputSchemaJson);
