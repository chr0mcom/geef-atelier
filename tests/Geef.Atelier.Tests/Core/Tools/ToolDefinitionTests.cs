using System.Text.Json;
using Geef.Atelier.Core.Domain.Tools;

namespace Geef.Atelier.Tests.Core.Tools;

public sealed class ToolDefinitionTests
{
    [Theory]
    [InlineData("web-search")]
    [InlineData("my-tool")]
    [InlineData("a")]
    [InlineData("tool123")]
    [InlineData("custom-my-tool")]
    [InlineData("abc")]
    public void IsValidName_ValidNames_ReturnsTrue(string name)
    {
        Assert.True(ToolDefinition.IsValidName(name));
    }

    [Theory]
    [InlineData("-starts-with-dash")]
    [InlineData("ends-with-")]
    [InlineData("Has_Underscore")]
    [InlineData("")]
    [InlineData("Has Space")]
    [InlineData("UPPERCASE")]
    public void IsValidName_InvalidNames_ReturnsFalse(string name)
    {
        Assert.False(ToolDefinition.IsValidName(name));
    }

    [Fact]
    public void ToolType_All_ContainsAllTypes()
    {
        var expected = new[]
        {
            ToolType.WebSearch,
            ToolType.KnowledgeBase,
            ToolType.UrlFetch,
            ToolType.NewsSearch,
            ToolType.AcademicSearch,
            ToolType.RestApi,
            ToolType.StaticContext,
            ToolType.LearningRetrieval,
            ToolType.McpTool
        };

        Assert.Equal(9, ToolType.All.Count);
        foreach (var type in expected)
        {
            Assert.Contains(type, ToolType.All);
        }
    }

    [Fact]
    public void ToolDefinition_IsSystem_ReadOnly_DefaultAccessClass()
    {
        var definition = new ToolDefinition(
            Name: "web-search",
            DisplayName: "Web Search",
            Description: "Searches the web via Tavily.",
            ToolType: ToolType.WebSearch,
            Settings: new Dictionary<string, string> { [ToolDefinitionSettingsKeys.MaxResults] = "5" },
            SecretRef: "TAVILY_API_KEY",
            LlmSchema: JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement,
            AccessClass: ToolAccessClass.ReadOnly,
            IsSystem: true
        );

        Assert.Equal("web-search", definition.Name);
        Assert.Equal("Web Search", definition.DisplayName);
        Assert.Equal(ToolAccessClass.ReadOnly, definition.AccessClass);
        Assert.True(definition.IsSystem);
        Assert.Equal("TAVILY_API_KEY", definition.SecretRef);
    }
}
