using System.Text.Json;
using System.Text.RegularExpressions;

namespace Geef.Atelier.Core.Domain.Tools;

/// <summary>
/// Immutable description of a tool that can be invoked by an LLM actor during a run.
/// </summary>
/// <param name="Name">
/// Lowercase kebab-case identifier. Must satisfy <see cref="IsValidName"/>.
/// System-owned tools use well-known names and are read-only.
/// </param>
/// <param name="DisplayName">Human-readable label shown in the UI.</param>
/// <param name="Description">Short prose description passed to the LLM in the tool list.</param>
/// <param name="ToolType">Discriminator string; use <see cref="Tools.ToolType"/> constants.</param>
/// <param name="Settings">Provider-specific key/value configuration; keys from <see cref="ToolDefinitionSettingsKeys"/>.</param>
/// <param name="SecretRef">
/// Optional reference to an environment variable that holds a secret (e.g. an API key).
/// Never stores the secret value itself.
/// </param>
/// <param name="LlmSchema">JSON Schema describing the input object the LLM must supply when calling this tool.</param>
/// <param name="AccessClass">Whether the tool is read-only or mutating.</param>
/// <param name="IsSystem">
/// <see langword="true"/> for built-in system tools that cannot be modified by users.
/// </param>
public sealed record ToolDefinition(
    string Name,
    string DisplayName,
    string Description,
    string ToolType,
    IReadOnlyDictionary<string, string> Settings,
    string? SecretRef,
    JsonElement LlmSchema,
    ToolAccessClass AccessClass,
    bool IsSystem
)
{
    private static readonly Regex NameRegex =
        new(@"^[a-z0-9]([a-z0-9\-]*[a-z0-9])?$", RegexOptions.Compiled);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="name"/> is a valid tool name.
    /// Valid names contain only lowercase ASCII letters, digits, and hyphens.
    /// They must start and end with a letter or digit (no leading or trailing hyphens).
    /// Minimum length is one character.
    /// </summary>
    public static bool IsValidName(string name) =>
        !string.IsNullOrEmpty(name) && NameRegex.IsMatch(name);
}

/// <summary>Well-known settings keys used in <see cref="ToolDefinition.Settings"/>.</summary>
public static class ToolDefinitionSettingsKeys
{
    /// <summary>API key value (prefer <see cref="ToolDefinition.SecretRef"/> for secrets).</summary>
    public const string ApiKey = "apiKey";

    /// <summary>Base URL for the upstream service.</summary>
    public const string BaseUrl = "baseUrl";

    /// <summary>Maximum number of results to return per invocation.</summary>
    public const string MaxResults = "maxResults";

    /// <summary>Full endpoint URL for REST / MCP tools.</summary>
    public const string Endpoint = "endpoint";

    /// <summary>Vector-store collection or index name.</summary>
    public const string CollectionName = "collectionName";

    /// <summary>Comma-separated list of domains to include in search results.</summary>
    public const string IncludeDomains = "includeDomains";

    /// <summary>Comma-separated list of domains to exclude from search results.</summary>
    public const string ExcludeDomains = "excludeDomains";

    /// <summary>BCP-47 language code for news search filtering.</summary>
    public const string NewsLanguage = "newsLanguage";

    /// <summary>Academic data source identifier: <c>arXiv</c>, <c>SemanticScholar</c>, or <c>OpenAlex</c>.</summary>
    public const string AcademicSource = "academicSource";

    /// <summary>JSONPath expression used to extract a value from a REST API response.</summary>
    public const string JsonPathExpression = "jsonPathExpression";

    /// <summary>HTTP method for REST API calls (e.g. <c>GET</c>, <c>POST</c>).</summary>
    public const string HttpMethod = "httpMethod";

    /// <summary>Inlined static text content for <c>static-context</c> tools.</summary>
    public const string StaticContent = "staticContent";

    /// <summary>Identifier of the grounding-refinement binding attached to this tool.</summary>
    public const string RefinementBinding = "refinementBinding";

    /// <summary>Refinement strategy mode (e.g. <c>summarize</c>, <c>extract</c>).</summary>
    public const string RefinementMode = "refinementMode";

    /// <summary>Free-text instructions passed to the refinement LLM pass.</summary>
    public const string RefinementInstructions = "refinementInstructions";

    /// <summary>
    /// Domain boost factor applied during learning-retrieval ranking.
    /// Numeric string; higher values prioritise matches from the current run domain.
    /// </summary>
    public const string DomainBoost = "domainBoost";

    /// <summary>ID (GUID string) of the <c>McpServerConfig</c> this tool was discovered from.</summary>
    public const string McpServerId = "mcpServerId";

    /// <summary>Original tool name as advertised by the MCP server (before sanitization).</summary>
    public const string McpOriginalName = "mcpOriginalName";
}
