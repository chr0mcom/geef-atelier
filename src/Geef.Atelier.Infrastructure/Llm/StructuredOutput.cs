namespace Geef.Atelier.Infrastructure.Llm;

/// <summary>
/// Helper for structured-JSON LLM calls. Uses OpenAI <c>response_format</c> json_schema when the
/// provider supports it (server-validated against the schema on the cli-proxy), otherwise falls back
/// to the forced single-tool JSON hack. Both response shapes are read back uniformly via
/// <see cref="ExtractJson"/>, so call sites stay provider-agnostic.
/// </summary>
internal static class StructuredOutput
{
    /// <summary>
    /// Returns the request fields for a structured-JSON call built from a tool definition:
    /// either a response_format (when supported) or forced tool-calling (fallback).
    /// </summary>
    public static (LlmTool[]? Tools, string? ToolChoice, LlmResponseFormat? ResponseFormat) Build(
        LlmTool tool, bool supportsResponseFormat)
        => supportsResponseFormat
            ? (null, null, LlmResponseFormat.JsonSchema(tool.Name, tool.InputSchema, strict: false))
            : ([tool], $"function:{tool.Name}", null);

    /// <summary>
    /// Extracts the JSON payload from either path: tool-call arguments (tool path) or message
    /// content (response_format path). Returns <see langword="null"/> when neither is present
    /// (e.g. a refusal).
    /// </summary>
    public static string? ExtractJson(LlmResponse response)
    {
        if (!string.IsNullOrWhiteSpace(response.ToolArgumentsJson))
            return response.ToolArgumentsJson;
        if (!string.IsNullOrWhiteSpace(response.Text))
            return response.Text;
        return null;
    }
}
