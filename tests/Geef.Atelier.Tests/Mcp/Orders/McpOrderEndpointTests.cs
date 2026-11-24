using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Geef.Atelier.Mcp;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Tests.Web.E2E;

namespace Geef.Atelier.Tests.Mcp.Orders;

/// <summary>The real <c>/mcp</c> endpoint sends the server instructions and lists the order tools.</summary>
[Collection("Postgres")]
public sealed class McpOrderEndpointTests(PostgresFixture pg) : IAsyncLifetime
{
    private WebTestHost _host = null!;

    public async Task InitializeAsync() =>
        _host = await WebTestHost.StartAsync(pg, initialGateCount: 100);

    public async Task DisposeAsync() =>
        await _host.DisposeAsync();

    [Fact]
    public async Task Initialize_answers_with_the_server_instructions()
    {
        var result = await PostAsync(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2025-06-18",
                capabilities = new { },
                clientInfo = new { name = "e2e-client", version = "1.0" },
            },
        });

        Assert.Equal(McpServerInstructions.Text, result.GetProperty("instructions").GetString());
    }

    [Fact]
    public async Task Tools_list_contains_the_order_tools_with_their_descriptions()
    {
        var result = await PostAsync(new { jsonrpc = "2.0", id = 1, method = "tools/list" });

        var tools = result.GetProperty("tools").EnumerateArray()
            .ToDictionary(t => t.GetProperty("name").GetString()!, t => t.GetProperty("description").GetString());
        Assert.Contains("submit_order", tools.Keys);
        Assert.Contains("get_order_status", tools.Keys);
        Assert.Contains("get_order_result", tools.Keys);
        Assert.Contains("submit_request", tools.Keys);
        Assert.Contains("in their own words", tools["submit_order"]);
        Assert.Contains("max 50", tools["get_order_status"]);
        Assert.Contains("Administrators only", tools["list_learnings"]);
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
