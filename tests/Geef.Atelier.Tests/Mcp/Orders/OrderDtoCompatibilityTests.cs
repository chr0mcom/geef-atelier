using System.Text.Json;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Mcp;
using Geef.Atelier.Mcp.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Tests.Mcp.Orders;

/// <summary>The order answers can grow (for example by questions for <c>needs_input</c>) without breaking older clients.</summary>
public sealed class OrderDtoCompatibilityTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Old_constructors_keep_working_and_new_fields_default_to_null()
    {
        var status = new OrderStatusDto("o", "working", false, "Writing", 3, 1, "r", [], 10);
        var run = new RunStatusDto("r", "Pending", null);

        Assert.Null(status.Outcome);
        Assert.Null(run.OrderId);
        Assert.Null(run.Warnings);
    }

    [Fact]
    public void A_future_needs_input_answer_with_extra_fields_still_reads_into_todays_types()
    {
        const string future = """
            {
              "orderId": "8a9b2f5e-0000-4000-8000-000000000001",
              "state": "needs_input",
              "runId": null,
              "message": "The order needs answers.",
              "sources": [],
              "questions": [{ "id": "q1", "text": "Who is the audience?", "options": ["Customers", "Staff"] }]
            }
            """;

        var submitted = JsonSerializer.Deserialize<OrderSubmittedDto>(future, Web)!;
        var status = JsonSerializer.Deserialize<OrderStatusDto>(future.Replace("\"message\"", "\"step\""), Web)!;

        Assert.Equal("needs_input", submitted.State);
        Assert.Null(submitted.RunId);
        Assert.Equal("needs_input", status.State);
        Assert.False(status.Done);
    }

    [Fact]
    public void Todays_answers_serialise_camel_case_with_snake_case_states()
    {
        var json = JsonSerializer.Serialize(
            new OrderSubmittedDto("o", OrderDisplayState.NeedsInput.ToWire(), null, "m", [new OrderSourceDto("a.pdf", 4, 1200, "good", [])]), Web);

        Assert.Contains("\"orderId\":\"o\"", json);
        Assert.Contains("\"state\":\"needs_input\"", json);
        Assert.Contains("\"runId\":null", json);
        Assert.Contains("\"fileName\":\"a.pdf\"", json);
    }

    [Fact]
    public void Wire_names_are_unique_and_snake_case()
    {
        var names = Enum.GetValues<OrderDisplayState>().Select(s => s.ToWire()).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.All(names, n => Assert.Matches("^[a-z]+(_[a-z]+)*$", n));
    }

    [Fact]
    public void Wire_names_match_the_order_state_resolver()
    {
        foreach (var state in Enum.GetValues<OrderDisplayState>())
            Assert.Equal(state.ToWireName(), state.ToWire());
    }

    [Fact]
    public void The_server_sends_instructions_to_clients()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAtelierMcp();
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.Equal(McpServerInstructions.Text, options.ServerInstructions);
    }

    [Theory]
    [InlineData("word for word")]
    [InlineData("Do not write your own version")]
    [InlineData("only into the attachments parameter")]
    [InlineData("Never send a placeholder")]
    [InlineData("Atelier web app")]
    [InlineData("1 to 10 minutes")]
    [InlineData("get_order_status with waitSeconds")]
    [InlineData("instead of sleeping")]
    [InlineData("Do not invent anything")]
    public void The_instructions_cover_the_agreed_points(string phrase) =>
        Assert.Contains(phrase, McpServerInstructions.Text);
}
