using Geef.Atelier.Mcp;
using Geef.Atelier.Mcp.Tools;
using ModelContextProtocol;

namespace Geef.Atelier.Tests.Mcp;

/// <summary>
/// The template studio is gone (stage 8c); its two MCP tools stay listed among the older tools so cached tool lists
/// of clients still resolve, but every call answers with an error that names submit_order.
/// </summary>
public sealed class RemovedTemplateStudioToolsTests
{
    [Fact]
    public void Both_tools_stay_among_the_older_tools()
    {
        Assert.Contains(typeof(AnalyzeTemplateProposalTool), McpToolCatalog.LegacyTools);
        Assert.Contains(typeof(MaterializeTemplateProposalTool), McpToolCatalog.LegacyTools);
    }

    [Fact]
    public void Analyze_answers_with_an_error_that_names_the_replacement()
    {
        var error = Assert.Throws<McpException>(() => AnalyzeTemplateProposalTool.AnalyzeTemplateProposal("Write a letter."));

        Assert.Equal(AnalyzeTemplateProposalTool.RemovedMessage, error.Message);
        Assert.Contains("submit_order", error.Message);
    }

    [Fact]
    public void Materialize_answers_with_an_error_that_names_the_replacement()
    {
        var error = Assert.Throws<McpException>(() => MaterializeTemplateProposalTool.MaterializeTemplateProposal(Guid.NewGuid().ToString()));

        Assert.Equal(AnalyzeTemplateProposalTool.RemovedMessage, error.Message);
    }

    [Fact]
    public void Neither_tool_needs_a_service()
    {
        var parameters = new[] { typeof(AnalyzeTemplateProposalTool), typeof(MaterializeTemplateProposalTool) }
            .ToolMethods()
            .SelectMany(m => m.GetParameters())
            .ToList();

        Assert.All(parameters, p => Assert.Equal(typeof(string), p.ParameterType));
    }
}
