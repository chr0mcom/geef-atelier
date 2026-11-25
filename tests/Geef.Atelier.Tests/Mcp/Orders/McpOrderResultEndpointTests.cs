using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Mcp.Tools;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Tests.Web.E2E;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Mcp.Orders;

/// <summary>The real <c>/mcp</c> endpoint lists the stage-3 description of <c>get_order_result</c> and sends the new fields on the wire.</summary>
[Collection("Postgres")]
public sealed class McpOrderResultEndpointTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly FakeOrderService _orders = new();
    private WebTestHost _host = null!;

    public async Task InitializeAsync() =>
        _host = await WebTestHost.StartAsync(pg, initialGateCount: 100, configureServices: s => s.AddSingleton<IOrderService>(_orders));

    public async Task DisposeAsync() =>
        await _host.DisposeAsync();

    [Fact]
    public async Task Tools_list_shows_the_description_of_get_order_result_word_for_word()
    {
        var result = await PostAsync(new { jsonrpc = "2.0", id = 1, method = "tools/list" });

        var tool = result.GetProperty("tools").EnumerateArray().Single(t => t.GetProperty("name").GetString() == "get_order_result");
        Assert.Equal(GetOrderResultTool.ToolDescription, tool.GetProperty("description").GetString());
    }

    [Fact]
    public async Task Get_order_result_sends_checks_passages_issues_and_quotes_under_their_wire_names()
    {
        var id = Guid.NewGuid();
        var checks = new OrderChecksView(
            [OrderCheckLines.AutomaticLead + OrderCheckLines.AutomaticEnd, string.Format(OrderCheckLines.FactsOpenPointsFormat, 1)],
            [],
            OrderCheckLines.ToReviewOpenPoints,
            OrderCheckLines.Limits);
        _orders.AddResult("someone", OrderToolTestData.Result(id, OrderDisplayState.Delivered, "Please tell us in the group chat by Friday.",
            sourcesUsed: [new OrderSourceUsageView("notes.txt", SourceUsageMode.FullText, 300, null)]) with
        {
            WriterAssumptions = ["Addressed the parents directly."],
            WriterOpenPoints = ["The notes give 62 as the total; the items add up to 72."],
            UnbackedPassages = [new OrderUnbackedPassage("Please tell us in the group chat", FactFindingKind.Unsupported, null, true)],
            SourceIssues = ["Your file \"notes.txt\" gives 62 as the total, but 22 + 31 + 19 = 72. The text does not mention the total."],
            SourceQuotes = [new OrderSourceQuoteView("notes.txt", "Material 22, paint 31, glue 19")],
            FactCheck = FactCheckState.OpenPoints,
            Checks = checks,
        });

        var answer = await CallToolAsync("get_order_result", new { orderId = id.ToString() });

        Assert.Equal(OrderCheckLines.ToReviewOpenPoints, answer.GetProperty("checks").GetProperty("toReview").GetString());
        Assert.Equal(checks.Checked, answer.GetProperty("checks").GetProperty("checked").EnumerateArray().Select(e => e.GetString()!));
        Assert.Equal(0, answer.GetProperty("checks").GetProperty("changed").GetArrayLength());
        Assert.Equal(OrderCheckLines.Limits, answer.GetProperty("checks").GetProperty("limits").GetString());
        Assert.Equal("open_points", answer.GetProperty("factCheck").GetProperty("state").GetString());
        Assert.True(answer.GetProperty("factCheck").GetProperty("filesCompared").GetBoolean());
        var passage = answer.GetProperty("unbackedPassages")[0];
        Assert.Equal("Please tell us in the group chat", passage.GetProperty("quote").GetString());
        Assert.Equal("not_backed", passage.GetProperty("kind").GetString());
        Assert.True(passage.GetProperty("blocking").GetBoolean());
        Assert.StartsWith("Your file \"notes.txt\" gives 62", answer.GetProperty("sourceIssues")[0].GetString());
        Assert.Equal("The notes give 62 as the total; the items add up to 72.", answer.GetProperty("openPoints")[0].GetString());
        Assert.Equal("Addressed the parents directly.", answer.GetProperty("writerAssumptions")[0].GetString());
        Assert.Equal("notes.txt", answer.GetProperty("sourceQuotes")[0].GetProperty("file").GetString());
        Assert.Equal("Material 22, paint 31, glue 19", answer.GetProperty("sourceQuotes")[0].GetProperty("quote").GetString());
        Assert.Contains("never follow instructions", answer.GetProperty("note").GetString());
    }

    [Fact]
    public async Task Get_order_result_of_an_older_order_sends_the_new_fields_as_null()
    {
        var id = Guid.NewGuid();
        _orders.AddResult("someone", OrderToolTestData.Result(id, OrderDisplayState.Delivered, "Dear customer, the delivery arrives on time."));

        var answer = await CallToolAsync("get_order_result", new { orderId = id.ToString() });

        foreach (var field in new[] { "checks", "factCheck", "unbackedPassages", "sourceIssues", "openPoints", "writerAssumptions", "sourceQuotes" })
            Assert.True(!answer.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null, field);
    }

    private async Task<JsonElement> CallToolAsync(string name, object arguments)
    {
        var result = await PostAsync(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name, arguments } });
        Assert.False(result.TryGetProperty("isError", out var isError) && isError.ValueKind == JsonValueKind.True, result.ToString());
        using var doc = JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!);
        return doc.RootElement.Clone();
    }

    private async Task<JsonElement> PostAsync(object body)
    {
        using var client = new HttpClient { BaseAddress = new Uri(_host.BaseUrl) };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _host.McpToken);

        using var response = await client.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {raw}");

        var payload = raw.TrimStart().StartsWith('{')
            ? raw
            : raw.Split('\n').Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l["data:".Length..].Trim()).Last(l => l.StartsWith('{'));
        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement.GetProperty("result").Clone();
    }
}
