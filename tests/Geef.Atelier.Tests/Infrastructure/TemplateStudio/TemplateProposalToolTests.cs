using System.Text.Json;
using Geef.Atelier.Infrastructure.TemplateStudio;

namespace Geef.Atelier.Tests.Infrastructure.TemplateStudio;

public sealed class TemplateProposalToolTests
{
    [Fact]
    public void ToolName_IsSubmitTemplateProposal()
    {
        Assert.Equal("submit_template_proposal", TemplateProposalTool.ToolName);
    }

    [Fact]
    public void Schema_Name_MatchesToolName()
    {
        Assert.Equal(TemplateProposalTool.ToolName, TemplateProposalTool.Schema.Name);
    }

    [Fact]
    public void Schema_InputSchema_IsValidJsonObject()
    {
        var schema = TemplateProposalTool.Schema.InputSchema;
        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
    }

    [Fact]
    public void Schema_Required_ContainsExpectedFields()
    {
        var schema = TemplateProposalTool.Schema.InputSchema;
        Assert.True(schema.TryGetProperty("required", out var required), "Schema must have a 'required' property.");
        var requiredFields = required.EnumerateArray().Select(e => e.GetString()).ToHashSet();

        Assert.Contains("matched_existing_templates", requiredFields);
        Assert.Contains("recommendation", requiredFields);
        Assert.Contains("reasoning_summary", requiredFields);
    }

    [Fact]
    public void MetaSystemPromptTemplate_ContainsPlaceholder()
    {
        Assert.Contains("{0}", TemplateStudioPrompts.MetaSystemPromptTemplate);
    }

    [Fact]
    public void MetaSystemPromptTemplate_ContainsToolName()
    {
        Assert.Contains("submit_template_proposal", TemplateStudioPrompts.MetaSystemPromptTemplate);
    }
}
