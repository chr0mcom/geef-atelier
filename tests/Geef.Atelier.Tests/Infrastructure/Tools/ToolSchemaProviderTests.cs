using System.Text.Json;
using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Infrastructure.Tools;

namespace Geef.Atelier.Tests.Infrastructure.Tools;

public sealed class ToolSchemaProviderTests
{
    private static readonly ToolSchemaProvider Provider = new();

    private static ToolDefinition MakeTool(
        string toolType,
        JsonElement? llmSchema = null) =>
        new(
            Name: "test-tool",
            DisplayName: "Test Tool",
            Description: "A tool for tests.",
            ToolType: toolType,
            Settings: new Dictionary<string, string>(),
            SecretRef: null,
            LlmSchema: llmSchema ?? JsonDocument.Parse("{}").RootElement,
            AccessClass: ToolAccessClass.ReadOnly,
            IsSystem: false);

    // -------------------------------------------------------------------------
    // Per-type schema smoke tests
    // -------------------------------------------------------------------------

    [Fact]
    public void GetSchema_WebSearch_ContainsQueryParam()
    {
        var schema = Provider.GetSchema(MakeTool(ToolType.WebSearch));

        Assert.Equal("test-tool", schema.Name);
        var doc = JsonDocument.Parse(schema.InputSchemaJson).RootElement;
        Assert.True(doc.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("query", out _), "Expected 'query' property");
    }

    [Fact]
    public void GetSchema_NewsSearch_ContainsQueryParam()
    {
        var schema = Provider.GetSchema(MakeTool(ToolType.NewsSearch));

        var doc = JsonDocument.Parse(schema.InputSchemaJson).RootElement;
        Assert.True(doc.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("query", out _));
    }

    [Fact]
    public void GetSchema_AcademicSearch_ContainsQueryParam()
    {
        var schema = Provider.GetSchema(MakeTool(ToolType.AcademicSearch));

        var doc = JsonDocument.Parse(schema.InputSchemaJson).RootElement;
        Assert.True(doc.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("query", out _));
    }

    [Fact]
    public void GetSchema_KnowledgeBase_ContainsQueryParam()
    {
        var schema = Provider.GetSchema(MakeTool(ToolType.KnowledgeBase));

        var doc = JsonDocument.Parse(schema.InputSchemaJson).RootElement;
        Assert.True(doc.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("query", out _));
    }

    [Fact]
    public void GetSchema_LearningRetrieval_ContainsQueryParam()
    {
        var schema = Provider.GetSchema(MakeTool(ToolType.LearningRetrieval));

        var doc = JsonDocument.Parse(schema.InputSchemaJson).RootElement;
        Assert.True(doc.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("query", out _));
    }

    [Fact]
    public void GetSchema_UrlFetch_ContainsUrlParam()
    {
        var schema = Provider.GetSchema(MakeTool(ToolType.UrlFetch));

        var doc = JsonDocument.Parse(schema.InputSchemaJson).RootElement;
        Assert.True(doc.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("url", out _), "Expected 'url' property");
    }

    [Fact]
    public void GetSchema_RestApi_ContainsEndpointAndParams()
    {
        var schema = Provider.GetSchema(MakeTool(ToolType.RestApi));

        var doc = JsonDocument.Parse(schema.InputSchemaJson).RootElement;
        Assert.True(doc.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("endpoint", out _), "Expected 'endpoint' property");
        Assert.True(props.TryGetProperty("params", out _), "Expected 'params' property");
    }

    [Fact]
    public void GetSchema_StaticContext_IsEmptyPropertiesObject()
    {
        var schema = Provider.GetSchema(MakeTool(ToolType.StaticContext));

        var doc = JsonDocument.Parse(schema.InputSchemaJson).RootElement;
        Assert.True(doc.TryGetProperty("properties", out var props));
        Assert.Empty(props.EnumerateObject());
    }

    [Fact]
    public void GetSchema_McpTool_UsesLlmSchemaFromDefinition()
    {
        const string mcpSchema =
            """{"type":"object","properties":{"city":{"type":"string","description":"City name"}},"required":["city"]}""";
        var schemaElement = JsonDocument.Parse(mcpSchema).RootElement;
        var tool = MakeTool(ToolType.McpTool, schemaElement);

        var schema = Provider.GetSchema(tool);

        // The schema JSON must be the MCP discovery result, not the generic fallback
        var doc = JsonDocument.Parse(schema.InputSchemaJson).RootElement;
        Assert.True(doc.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("city", out _), "Expected MCP-specific 'city' property");
    }

    [Fact]
    public void GetSchema_McpTool_EmptyLlmSchema_FallsBackToGeneric()
    {
        var tool = MakeTool(ToolType.McpTool); // LlmSchema = {}

        var schema = Provider.GetSchema(tool);

        var doc = JsonDocument.Parse(schema.InputSchemaJson).RootElement;
        Assert.True(doc.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("input", out _), "Expected generic 'input' fallback property");
    }

    [Fact]
    public void GetSchema_CustomLlmSchema_TakesPrecedenceOverToolType()
    {
        // A web-search tool with a non-empty custom LlmSchema should use the custom schema,
        // not the built-in "query"-based web-search schema.
        const string customSchema =
            """{"type":"object","properties":{"keywords":{"type":"array","items":{"type":"string"}}},"required":["keywords"]}""";
        var schemaElement = JsonDocument.Parse(customSchema).RootElement;
        var tool = MakeTool(ToolType.WebSearch, schemaElement);

        var schema = Provider.GetSchema(tool);

        var doc = JsonDocument.Parse(schema.InputSchemaJson).RootElement;
        Assert.True(doc.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("keywords", out _), "Expected custom 'keywords' property");
        // Must NOT have the built-in 'query' property
        Assert.False(props.TryGetProperty("query", out _), "Built-in 'query' must be overridden by custom schema");
    }

    [Fact]
    public void GetSchema_UnknownToolType_ReturnsGenericSchema()
    {
        var tool = MakeTool("unknown-future-type");

        var schema = Provider.GetSchema(tool);

        var doc = JsonDocument.Parse(schema.InputSchemaJson).RootElement;
        Assert.True(doc.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("input", out _), "Expected generic 'input' fallback");
    }

    [Fact]
    public void GetSchema_PreservesToolNameAndDescription()
    {
        // ToolSchema must forward Name and Description from the ToolDefinition.
        var tool = new ToolDefinition(
            Name: "my-special-tool",
            DisplayName: "My Special Tool",
            Description: "Does something special.",
            ToolType: ToolType.WebSearch,
            Settings: new Dictionary<string, string>(),
            SecretRef: null,
            LlmSchema: JsonDocument.Parse("{}").RootElement,
            AccessClass: ToolAccessClass.ReadOnly,
            IsSystem: false);

        var schema = Provider.GetSchema(tool);

        Assert.Equal("my-special-tool", schema.Name);
        Assert.Equal("Does something special.", schema.Description);
    }
}
