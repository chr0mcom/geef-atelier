using System.Text.Json;
using Geef.Atelier.Application.Tools;
using Geef.Atelier.Core.Domain.Tools;

namespace Geef.Atelier.Infrastructure.Tools;

/// <summary>
/// Produces <see cref="ToolSchema"/> descriptors for <see cref="ToolDefinition"/> instances.
/// When the definition carries a non-empty custom <see cref="ToolDefinition.LlmSchema"/> that
/// overrides the built-in type defaults.
/// </summary>
internal sealed class ToolSchemaProvider : IToolSchemaProvider
{
    // Shared query-based schema used by search-type tools
    private const string QuerySchema =
        """{"type":"object","properties":{"query":{"type":"string","description":"Search query"}},"required":["query"]}""";

    private const string UrlFetchSchema =
        """{"type":"object","properties":{"url":{"type":"string","description":"URL to fetch (must be public)"}},"required":["url"]}""";

    private const string RestApiSchema =
        """{"type":"object","properties":{"endpoint":{"type":"string"},"params":{"type":"object","additionalProperties":{"type":"string"}}},"required":["endpoint"]}""";

    private const string StaticContextSchema =
        """{"type":"object","properties":{},"required":[]}""";

    private const string GenericSchema =
        """{"type":"object","properties":{"input":{"type":"string"}},"required":["input"]}""";

    /// <inheritdoc/>
    public ToolSchema GetSchema(ToolDefinition tool)
    {
        // Custom LlmSchema on the definition takes precedence when it is a non-empty JSON object.
        var customJson = TryGetCustomSchemaJson(tool.LlmSchema);
        if (customJson is not null)
            return new ToolSchema(tool.Name, tool.Description, customJson);

        var inputSchemaJson = tool.ToolType switch
        {
            ToolType.WebSearch       => QuerySchema,
            ToolType.NewsSearch      => QuerySchema,
            ToolType.AcademicSearch  => QuerySchema,
            ToolType.KnowledgeBase   => QuerySchema,
            ToolType.LearningRetrieval => QuerySchema,
            ToolType.UrlFetch        => UrlFetchSchema,
            ToolType.RestApi         => RestApiSchema,
            ToolType.StaticContext   => StaticContextSchema,
            _                        => GenericSchema
        };

        return new ToolSchema(tool.Name, tool.Description, inputSchemaJson);
    }

    /// <summary>
    /// Returns the raw JSON text when <paramref name="schema"/> is a non-empty JSON object;
    /// <see langword="null"/> otherwise.
    /// </summary>
    private static string? TryGetCustomSchemaJson(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object)
            return null;

        // An empty object {} is not considered a meaningful custom schema.
        if (!schema.EnumerateObject().Any())
            return null;

        return schema.GetRawText();
    }
}
