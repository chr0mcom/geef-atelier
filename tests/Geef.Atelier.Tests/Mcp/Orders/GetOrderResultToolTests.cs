using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Mcp.Models;
using Geef.Atelier.Mcp.Tools;
using ModelContextProtocol;

namespace Geef.Atelier.Tests.Mcp.Orders;

public sealed class GetOrderResultToolTests
{
    private const string Text = "Dear customer, your delivery will arrive two weeks later than planned. Kind regards, [Name]";

    private readonly FakeOrderService _orders = new();
    private readonly FakeUser _alice = new("alice");

    [Fact]
    public async Task A_delivered_order_returns_text_placeholders_files_and_warnings()
    {
        var id = Guid.NewGuid();
        _orders.AddResult("alice", OrderToolTestData.Result(id, OrderDisplayState.Delivered, Text,
            placeholders: ["[Name]"],
            sourcesUsed: [new OrderSourceUsageView("offer.pdf", SourceUsageMode.FullTextWithMap, 412_000, null), new OrderSourceUsageView("notes.md", SourceUsageMode.FullText, 800, null)],
            warnings: [OrderToolTestData.Warning("grounding.source_warning", "Page 3 of offer.pdf has no text.")]));

        var dto = await GetOrderResultTool.GetOrderResult(_orders, _alice, id.ToString());

        Assert.Equal(id.ToString(), dto.OrderId);
        Assert.Equal("delivered", dto.State);
        Assert.Equal(Text, dto.Text);
        Assert.Equal(14, dto.WordCount);
        Assert.Equal(1, dto.Version);
        Assert.Equal(["[Name]"], dto.Placeholders);
        Assert.Empty(dto.OpenChecks);
        Assert.Equal(2, dto.SourcesUsed.Count);
        Assert.Equal(("offer.pdf", "full_text_with_map", 412_000), (dto.SourcesUsed[0].FileName, dto.SourcesUsed[0].Mode, dto.SourcesUsed[0].Characters));
        Assert.Equal("full_text", dto.SourcesUsed[1].Mode);
        Assert.Equal("Page 3 of offer.pdf has no text.", Assert.Single(dto.Warnings).Message);
        Assert.Contains("placeholders", dto.Note);
    }

    [Fact]
    public async Task The_assumptions_of_the_writer_come_with_the_result()
    {
        var id = Guid.NewGuid();
        _orders.AddResult("alice", OrderToolTestData.Result(id, OrderDisplayState.Delivered, Text,
            assumptions: [OrderToolTestData.ToneAssumption, OrderToolTestData.DateAssumption]));

        var dto = await GetOrderResultTool.GetOrderResult(_orders, _alice, id.ToString());

        Assert.Equal(
            new[] { new AssumptionDto("a1", "Freundlicher, sachlicher Ton", false), new AssumptionDto("a2", "Neuer Liefertermin als Platzhalter", true) },
            dto.Assumptions!);
    }

    [Fact]
    public async Task An_order_without_clarification_has_no_assumptions()
    {
        var id = Guid.NewGuid();
        _orders.AddResult("alice", OrderToolTestData.Result(id, OrderDisplayState.Delivered, Text));

        var dto = await GetOrderResultTool.GetOrderResult(_orders, _alice, id.ToString());

        Assert.Null(dto.Assumptions);
    }

    [Fact]
    public async Task Open_points_still_return_the_text_with_the_open_checks_and_a_note()
    {
        var id = Guid.NewGuid();
        _orders.AddResult("alice", OrderToolTestData.Result(id, OrderDisplayState.DeliveredWithOpenPoints, Text,
            openChecks: [new DraftCheckResult(DraftCheckCodes.Length, false, true,
                "The text has 263 words. The request asks for at most 200 words (\"max. 200 Wörter\").", "max. 200 Wörter")]));

        var dto = await GetOrderResultTool.GetOrderResult(_orders, _alice, id.ToString());

        Assert.Equal("delivered_with_open_points", dto.State);
        Assert.Equal(Text, dto.Text);
        Assert.Equal(["The text has 263 words. The request asks for at most 200 words (\"max. 200 Wörter\")."], dto.OpenChecks);
        Assert.Contains("openChecks", dto.Note);
    }

    [Fact]
    public async Task A_working_order_has_no_text_and_says_to_wait()
    {
        var id = Guid.NewGuid();
        _orders.AddResult("alice", OrderToolTestData.Result(id, OrderDisplayState.Working, null));

        var dto = await GetOrderResultTool.GetOrderResult(_orders, _alice, id.ToString());

        Assert.Equal("working", dto.State);
        Assert.Null(dto.Text);
        Assert.Contains("get_order_status", dto.Note);
    }

    [Fact]
    public async Task A_failed_order_says_that_no_text_was_written()
    {
        var id = Guid.NewGuid();
        _orders.AddResult("alice", OrderToolTestData.Result(id, OrderDisplayState.Failed, null));

        var dto = await GetOrderResultTool.GetOrderResult(_orders, _alice, id.ToString());

        Assert.Equal("failed", dto.State);
        Assert.Null(dto.Text);
        Assert.Contains("No text was written", dto.Note);
    }

    [Fact]
    public async Task A_stopped_order_with_text_still_returns_the_text()
    {
        var id = Guid.NewGuid();
        _orders.AddResult("alice", OrderToolTestData.Result(id, OrderDisplayState.Stopped, Text));

        var dto = await GetOrderResultTool.GetOrderResult(_orders, _alice, id.ToString());

        Assert.Equal("stopped", dto.State);
        Assert.Equal(Text, dto.Text);
        Assert.Contains("stopped", dto.Note);
    }

    [Fact]
    public async Task A_delivered_text_without_placeholders_needs_no_note()
    {
        var id = Guid.NewGuid();
        _orders.AddResult("alice", OrderToolTestData.Result(id, OrderDisplayState.Delivered, "Dear customer, the delivery arrives on time."));

        var dto = await GetOrderResultTool.GetOrderResult(_orders, _alice, id.ToString());

        Assert.Null(dto.Note);
    }

    [Fact]
    public async Task The_note_of_the_service_wins()
    {
        var id = Guid.NewGuid();
        _orders.AddResult("alice", OrderToolTestData.Result(id, OrderDisplayState.Delivered, Text, note: "Version 2 replaces version 1."));

        var dto = await GetOrderResultTool.GetOrderResult(_orders, _alice, id.ToString());

        Assert.Equal("Version 2 replaces version 1.", dto.Note);
    }

    [Fact]
    public async Task A_foreign_order_is_not_found_but_an_administrator_sees_it()
    {
        var id = Guid.NewGuid();
        _orders.AddResult("bob", OrderToolTestData.Result(id, OrderDisplayState.Delivered, Text));

        var ex = await Assert.ThrowsAsync<McpException>(() => GetOrderResultTool.GetOrderResult(_orders, _alice, id.ToString()));
        Assert.Equal("Order not found.", ex.Message);

        var dto = await GetOrderResultTool.GetOrderResult(_orders, new FakeUser("root", isAdmin: true), id.ToString());
        Assert.Equal(Text, dto.Text);
    }

    [Fact]
    public void Description_says_that_the_text_also_comes_with_open_checks() =>
        Assert.Contains("Also returns the text when some checks are open", GetOrderResultTool.ToolDescription);
}
