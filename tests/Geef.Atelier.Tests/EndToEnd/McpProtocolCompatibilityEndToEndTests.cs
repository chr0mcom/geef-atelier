using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Geef.Atelier.Mcp;
using Geef.Atelier.Tests.Persistence;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit.Abstractions;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// The production wiring (<c>Program</c>) on the real Kestrel server with SDK 2.4.0 (D-086): clients of the older revision 2025-11-25
/// (and 2025-06-18) still initialize, list the tools and call one, without a session, while the SDK client of today negotiates
/// 2026-07-28. A wait of 50 seconds in <c>get_order_status</c>, during which the server sends nothing, comes back complete for both.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class McpProtocolCompatibilityEndToEndTests(PostgresFixture postgres, ITestOutputHelper output)
{
    private const string OlderRevision = "2025-11-25";
    private const string CurrentRevision = "2026-07-28";

    private static readonly string[] OrderTools =
        ["answer_order", "cancel_order", "export_order", "get_order_result", "get_order_status", "list_orders", "prepare_attachment", "profile", "revise_order", "submit_order"];

    [Theory]
    [InlineData(OlderRevision)]
    [InlineData("2025-06-18")]
    public async Task An_older_client_initializes_lists_the_tools_and_calls_one_without_a_session(string revision)
    {
        var port = FreePort();
        await using var host = await AtelierEndToEndHost.StartAsync(postgres, kestrelPort: port);
        using var http = Client(port);

        var (initialize, init) = await PostAsync(http, null, "initialize", new
        {
            protocolVersion = revision,
            capabilities = new { },
            clientInfo = new { name = "older-client", version = "1.0" },
        });
        Assert.Equal(HttpStatusCode.OK, init.StatusCode);
        Assert.False(init.Headers.Contains("Mcp-Session-Id"));
        var result = initialize.GetProperty("result");
        Assert.Equal(revision, result.GetProperty("protocolVersion").GetString());
        var instructions = result.GetProperty("instructions").GetString()!;
        Assert.True(instructions.Length <= McpServerInstructions.TargetLength, $"{instructions.Length} characters");
        Assert.StartsWith("Atelier writes finished texts", instructions, StringComparison.Ordinal);
        var capabilities = result.GetProperty("capabilities");
        Assert.True(capabilities.TryGetProperty("tools", out _));
        Assert.Equal(["io.modelcontextprotocol/skills"], capabilities.GetProperty("extensions").EnumerateObject().Select(e => e.Name));
        Assert.False(capabilities.TryGetProperty("tasks", out _));

        using var initialized = await SendAsync(http, revision, new { jsonrpc = "2.0", method = "notifications/initialized" });
        Assert.Equal(HttpStatusCode.Accepted, initialized.StatusCode);

        var (list, _) = await PostAsync(http, revision, "tools/list", new { });
        var tools = list.GetProperty("result").GetProperty("tools").EnumerateArray().ToList();
        foreach (var name in OrderTools)
            Assert.Contains(tools, t => t.GetProperty("name").GetString() == name);
        Assert.DoesNotContain(tools, t => t.TryGetProperty("execution", out _));

        var (call, _) = await PostAsync(http, revision, "tools/call", new { name = "list_orders", arguments = new { limit = 5 } });
        var callResult = call.GetProperty("result");
        Assert.False(callResult.TryGetProperty("isError", out var isError) && isError.GetBoolean(), callResult.ToString());
        Assert.Equal(JsonValueKind.Array, ToolText(callResult.GetProperty("content")[0].GetProperty("text").GetString()!).ValueKind);
    }

    [Theory]
    [InlineData(OlderRevision, OlderRevision)]
    [InlineData(null, CurrentRevision)]
    public async Task The_sdk_client_negotiates_the_revision_and_calls_a_tool(string? requested, string negotiated)
    {
        var port = FreePort();
        await using var host = await AtelierEndToEndHost.StartAsync(postgres, kestrelPort: port);
        using var http = Client(port);

        await using var client = await ConnectAsync(http, port, requested);

        Assert.Equal(negotiated, client.NegotiatedProtocolVersion);
        Assert.True(client.ServerInstructions!.Length <= McpServerInstructions.TargetLength, $"{client.ServerInstructions.Length} characters");
        var tools = await client.ListToolsAsync();
        foreach (var name in OrderTools)
            Assert.Contains(tools, t => t.Name == name);
        var called = await client.CallToolAsync("list_orders", new Dictionary<string, object?> { ["limit"] = 5 });
        Assert.NotEqual(true, called.IsError);
    }

    [Fact]
    public async Task A_silent_wait_of_fifty_seconds_comes_back_complete_for_older_and_current_clients()
    {
        var port = FreePort();
        await using var host = await AtelierEndToEndHost.StartAsync(postgres, kestrelPort: port);
        var submitted = await host.CallMcpToolAsync("submit_order", new { request = "Write a short note to the team about the new opening hours of the office." });
        var orderId = submitted.GetProperty("orderId").GetString()!;
        Assert.Equal("queued", submitted.GetProperty("state").GetString());

        var olderTiming = new TimingHandler();
        var currentTiming = new TimingHandler();
        using var olderHttp = Client(port, olderTiming);
        using var currentHttp = Client(port, currentTiming);
        await using var current = await ConnectAsync(currentHttp, port, null);
        Assert.Equal(CurrentRevision, current.NegotiatedProtocolVersion);

        var older = MeasureAsync(async () =>
        {
            var (answer, response) = await PostAsync(olderHttp, OlderRevision, "tools/call",
                new { name = "get_order_status", arguments = new { orderId, waitSeconds = 50 } }, olderTiming);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = answer.GetProperty("result");
            Assert.False(result.TryGetProperty("isError", out var isError) && isError.GetBoolean(), result.ToString());
            return ToolText(result.GetProperty("content")[0].GetProperty("text").GetString()!);
        });
        var latest = MeasureAsync(async () =>
        {
            currentTiming.Start();
            var result = await current.CallToolAsync("get_order_status", new Dictionary<string, object?> { ["orderId"] = orderId, ["waitSeconds"] = 50 });
            Assert.NotEqual(true, result.IsError);
            return ToolText(Assert.IsType<TextContentBlock>(result.Content[0]).Text);
        });
        var answers = await Task.WhenAll(older, latest);

        foreach (var (label, (result, elapsed), timing) in new[] { ("2025-11-25", answers[0], olderTiming), ("2026-07-28", answers[1], currentTiming) })
        {
            output.WriteLine($"{label}: answer after {elapsed.TotalSeconds:F1} s, response headers after {timing.HeadersAfter?.TotalSeconds:F1} s");
            Assert.True(elapsed >= TimeSpan.FromSeconds(49), $"{label}: {elapsed}");
            Assert.True(elapsed < TimeSpan.FromSeconds(60), $"{label}: {elapsed}");
            Assert.Equal("queued", result.GetProperty("state").GetString());
            Assert.False(result.GetProperty("done").GetBoolean());
        }
    }

    private static JsonElement ToolText(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static async Task<(T Result, TimeSpan Elapsed)> MeasureAsync<T>(Func<Task<T>> action)
    {
        var watch = Stopwatch.StartNew();
        var result = await action();
        return (result, watch.Elapsed);
    }

    private static HttpClient Client(int port, HttpMessageHandler? handler = null) =>
        new(handler ?? new SocketsHttpHandler(), disposeHandler: true)
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
            Timeout = TimeSpan.FromMinutes(3),
        };

    private static Task<McpClient> ConnectAsync(HttpClient http, int port, string? protocolVersion) =>
        McpClient.CreateAsync(
            new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Endpoint = new Uri($"http://127.0.0.1:{port}/mcp"),
                    TransportMode = HttpTransportMode.StreamableHttp,
                    AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + AtelierEndToEndHost.McpToken },
                },
                http, ownsHttpClient: false),
            new McpClientOptions
            {
                ProtocolVersion = protocolVersion,
                ClientInfo = new Implementation { Name = "compatibility-test", Version = "1.0" },
            });

    private static async Task<(JsonElement Answer, HttpResponseMessage Response)> PostAsync(
        HttpClient http, string? revision, string method, object parameters, TimingHandler? timing = null)
    {
        timing?.Start();
        var response = await SendAsync(http, revision, new { jsonrpc = "2.0", id = 1, method, @params = parameters });
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{method}: {(int)response.StatusCode} {raw}");
        using var document = JsonDocument.Parse(JsonRpcPayload(raw));
        return (document.RootElement.Clone(), response);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient http, string? revision, object message)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(JsonSerializer.Serialize(message), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AtelierEndToEndHost.McpToken);
        if (revision is not null)
            request.Headers.Add("MCP-Protocol-Version", revision);
        return await http.SendAsync(request);
    }

    private static string JsonRpcPayload(string raw)
    {
        var trimmed = raw.TrimStart();
        if (trimmed.StartsWith('{'))
            return trimmed;
        return raw.Split('\n')
            .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
            .Select(line => line["data:".Length..].Trim())
            .Last(line => line.StartsWith('{'));
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class TimingHandler() : DelegatingHandler(new SocketsHttpHandler())
    {
        private readonly Stopwatch _watch = new();

        public TimeSpan? HeadersAfter { get; private set; }

        public void Start()
        {
            HeadersAfter = null;
            _watch.Restart();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (_watch.IsRunning && request.Method == HttpMethod.Post)
                HeadersAfter = _watch.Elapsed;
            return response;
        }
    }
}
