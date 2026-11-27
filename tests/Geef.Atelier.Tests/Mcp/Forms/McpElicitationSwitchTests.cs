using System.Text.Json;
using System.Text.Json.Nodes;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Tests.Mcp.Forms;

/// <summary>The switch <c>Mcp:Elicitation</c> (D-088): off, the server is the same as before; on, only the description of <c>submit_order</c> gains one sentence.</summary>
public sealed class McpElicitationSwitchTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("On")]
    public void Off_changes_neither_tools_nor_instructions_nor_capabilities(string? tickets)
    {
        using var unset = McpTestServer.Provider(tickets);
        using var off = McpTestServer.Provider(tickets, configure: s => s.Configure<McpOptions>(o => o.Elicitation = McpElicitationMode.Off));

        Assert.Equal(McpTestServer.ToolListJson(unset), McpTestServer.ToolListJson(off));
        Assert.Equal(McpTestServer.Instructions(unset), McpTestServer.Instructions(off));
        Assert.Null(off.GetRequiredService<IOptions<McpServerOptions>>().Value.Capabilities);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("On")]
    public void On_adds_one_sentence_to_submit_order_and_nothing_else(string? tickets)
    {
        using var off = McpTestServer.Provider(tickets);
        using var on = McpTestServer.Provider(tickets, configure: s => s.Configure<McpOptions>(o => o.Elicitation = McpElicitationMode.On));

        Assert.Equal(McpTestServer.Instructions(off), McpTestServer.Instructions(on));
        Assert.Null(on.GetRequiredService<IOptions<McpServerOptions>>().Value.Capabilities);
        var before = McpTestServer.Tools(off);
        var after = McpTestServer.Tools(on);
        foreach (var name in before.Keys)
        {
            var expected = Node(before[name]);
            if (name == "submit_order")
                expected["description"] = before[name].ProtocolTool.Description + SubmitOrderTool.FormSentence;
            Assert.Equal(expected.ToJsonString(), Node(after[name]).ToJsonString());
        }
    }

    [Fact]
    public void The_description_with_both_switches_off_is_the_one_before()
    {
        Assert.Equal(SubmitOrderTool.ToolDescription, SubmitOrderTool.Describe(false, new McpOptions()));
        Assert.Equal(SubmitOrderTool.TicketToolDescription, SubmitOrderTool.Describe(true, new McpOptions()));
        Assert.Equal(SubmitOrderTool.ToolDescription + SubmitOrderTool.SkillSentence + SubmitOrderTool.FormSentence,
            SubmitOrderTool.Describe(false, new McpOptions { Skills = McpSkillsMode.On, Elicitation = McpElicitationMode.On }));
    }

    private static JsonObject Node(McpServerTool tool) =>
        JsonNode.Parse(JsonSerializer.Serialize(tool.ProtocolTool, McpJsonUtilities.DefaultOptions))!.AsObject();
}
