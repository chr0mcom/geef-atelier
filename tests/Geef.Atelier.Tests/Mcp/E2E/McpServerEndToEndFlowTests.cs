using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Tests.Web.E2E;

namespace Geef.Atelier.Tests.Mcp.E2E;

[Collection("Postgres")]
public sealed class McpServerEndToEndFlowTests(PostgresFixture pg) : IAsyncLifetime
{
    private WebTestHost _host = null!;

    public async Task InitializeAsync() =>
        _host = await WebTestHost.StartAsync(pg, initialGateCount: 100);

    public async Task DisposeAsync() =>
        await _host.DisposeAsync();

    /// <summary>
    /// Creates an HTTP request for the MCP Streamable HTTP transport.
    /// Per spec: POST must include Accept: application/json, text/event-stream
    /// and Content-Type: application/json.
    /// </summary>
    private static HttpRequestMessage CreateMcpRequest(object body, string? bearerToken = null)
    {
        var json = JsonSerializer.Serialize(body);
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        // MCP Streamable HTTP transport requires both JSON and SSE in Accept header
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        if (bearerToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return request;
    }

    [Fact]
    public async Task McpToolsList_WithValidToken_Returns200AndToolList()
    {
        using var client = new HttpClient { BaseAddress = new Uri(_host.BaseUrl) };

        var request = CreateMcpRequest(
            new { jsonrpc = "2.0", method = "tools/list", id = 1 },
            bearerToken: _host.McpToken);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("submit_request", body);
    }

    [Fact]
    public async Task McpEndpoint_WithInvalidToken_Returns401()
    {
        using var client = new HttpClient { BaseAddress = new Uri(_host.BaseUrl) };

        var request = CreateMcpRequest(
            new { jsonrpc = "2.0", method = "tools/list", id = 1 },
            bearerToken: "wrong-token");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
