using System.IO.Pipelines;
using System.Text.Json;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Mcp;
using Geef.Atelier.Mcp.Tools;
using Geef.Atelier.Tests.Mcp.Orders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Tests.Mcp;

/// <summary>
/// The switch <c>Uploads:Tickets</c> through the real MCP protocol (SDK server and client over an in-memory stream): what a client
/// lists and reads with the switch off and on, that an <c>uploadIds</c> argument the schema does not offer still reaches the tool,
/// and that the Base64 way of <c>attachments</c> works as before.
/// </summary>
public sealed class McpTicketsProtocolTests
{
    private readonly FakeOrderService _orders = new();
    private readonly FakeSourceIntake _intake = new();
    private readonly FakeRefusalLog _refusals = new();

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public async Task Without_tickets_the_client_sees_the_tool_list_and_instructions_of_before(string legacy)
    {
        await using var session = await Session.StartAsync(Services(tickets: false, legacy));

        var tools = await session.Client.ListToolsAsync();

        Assert.DoesNotContain(tools, t => t.Name == "prepare_attachment");
        Assert.DoesNotContain(SubmitOrderTool.UploadIdsParameter, Properties(tools.Single(t => t.Name == "submit_order").ProtocolTool));
        using var reference = McpTestServer.Provider(legacy, tickets: null);
        Assert.Equal(McpTestServer.ToolListJson(reference), ToolListJson(tools));
        Assert.Equal(McpTestServer.Instructions(reference), session.Client.ServerInstructions);
        Assert.Contains("ask the person to start the order in the Atelier web app", session.Client.ServerInstructions, StringComparison.Ordinal);
        Assert.DoesNotContain("prepare_attachment", session.Client.ServerInstructions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_tickets_the_client_sees_prepare_attachment_upload_ids_and_the_ticket_step()
    {
        await using var session = await Session.StartAsync(Services(tickets: true, legacy: "false"));

        var tools = await session.Client.ListToolsAsync();

        Assert.Equal(10, tools.Count);
        var prepare = tools.Single(t => t.Name == "prepare_attachment").ProtocolTool;
        Assert.Equal(PrepareAttachmentTool.ToolDescription, prepare.Description);
        Assert.Equal(["fileName", "sizeBytes"], Properties(prepare));
        Assert.Equal(["fileName"], Required(prepare));
        var submit = tools.Single(t => t.Name == "submit_order").ProtocolTool;
        Assert.Equal(["request", "attachments", "extraCareful", "uploadIds"], Properties(submit));
        Assert.Equal(["request"], Required(submit));
        var uploadIds = submit.InputSchema.GetProperty("properties").GetProperty("uploadIds");
        Assert.Equal(SubmitOrderTool.UploadIdsDescription, uploadIds.GetProperty("description").GetString());
        Assert.Contains("string", Types(uploadIds.GetProperty("items")));
        Assert.Contains("array", Types(uploadIds));
        Assert.Equal(McpServerInstructions.For(new OrderOptions().MaxWaitSeconds, false, tickets: true), session.Client.ServerInstructions);
        Assert.Contains(McpServerInstructions.TicketFilesStep, session.Client.ServerInstructions, StringComparison.Ordinal);
        Assert.DoesNotContain(McpServerInstructions.AttachmentFilesStep, session.Client.ServerInstructions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_tickets_no_text_tells_the_client_to_start_without_uploading_or_to_send_files_as_base64()
    {
        await using var session = await Session.StartAsync(Services(tickets: true, legacy: "false"));

        var tools = await session.Client.ListToolsAsync();
        var submit = tools.Single(t => t.Name == "submit_order").ProtocolTool;
        var attachments = submit.InputSchema.GetProperty("properties").GetProperty(SubmitOrderTool.AttachmentsParameter).GetProperty("description").GetString();
        var instructions = session.Client.ServerInstructions!;

        Assert.Equal(SubmitOrderTool.TicketToolDescription, submit.Description);
        Assert.Equal(SubmitOrderTool.TicketAttachmentsDescription, attachments);
        Assert.Contains("upload each file first with prepare_attachment", submit.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("contentBase64 must be the complete original file. Supported content types: application/pdf", submit.Description, StringComparison.Ordinal);
        Assert.StartsWith("Optional, only for very small text files", attachments, StringComparison.Ordinal);
        Assert.DoesNotContain("1. Start with submit_order", instructions, StringComparison.Ordinal);
        Assert.Contains(McpServerInstructions.TicketStartStep, instructions, StringComparison.Ordinal);
        Assert.True(instructions.IndexOf("prepare_attachment", StringComparison.Ordinal) < instructions.IndexOf("2. For each file", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_tickets_submit_order_keeps_its_description_and_the_base64_attachments()
    {
        await using var session = await Session.StartAsync(Services(tickets: false, legacy: "false"));

        var submit = (await session.Client.ListToolsAsync()).Single(t => t.Name == "submit_order").ProtocolTool;

        Assert.Equal(SubmitOrderTool.ToolDescription, submit.Description);
        Assert.StartsWith("Optional files as a JSON array", submit.InputSchema.GetProperty("properties").GetProperty("attachments").GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("Atelier writes finished texts for people (letters, emails, articles, summaries, reports) from their request and their files.\n1. Start with submit_order. Pass the person's request word for word",
            session.Client.ServerInstructions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_tickets_and_older_tools_the_server_offers_all_twenty_nine()
    {
        await using var session = await Session.StartAsync(Services(tickets: true, legacy: "true"));

        var tools = await session.Client.ListToolsAsync();

        Assert.Equal(29, tools.Count);
        Assert.EndsWith(McpServerInstructions.LegacyLine, session.Client.ServerInstructions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_tickets_an_upload_ids_argument_still_reaches_the_tool_and_gets_the_fixed_sentence()
    {
        await using var session = await Session.StartAsync(Services(tickets: false, legacy: "false"));

        var result = await session.Client.CallToolAsync("submit_order", new Dictionary<string, object?>
        {
            ["request"] = "Fasse die Datei zusammen.",
            ["uploadIds"] = new[] { Guid.NewGuid().ToString() },
        });

        Assert.True(result.IsError);
        Assert.Equal(Error("submit_order", UploadTicketLines.NotAvailable), Text(result));
        Assert.Empty(_orders.Submitted);
    }

    [Fact]
    public async Task Without_tickets_the_base64_way_works_as_before()
    {
        await using var session = await Session.StartAsync(Services(tickets: false, legacy: "false"));

        var result = await session.Client.CallToolAsync("submit_order", new Dictionary<string, object?>
        {
            ["request"] = "Fasse die Datei zusammen.",
            ["attachments"] = Attachments.Json(("offer.pdf", "application/pdf", Attachments.MinimalPdf)),
        });

        Assert.NotEqual(true, result.IsError);
        Assert.Equal(Attachments.MinimalPdf, Assert.Single(_intake.Intakes).Content);
        Assert.Single(Assert.Single(_orders.Submitted).Sources!);
        Assert.Contains("\"fileName\":\"offer.pdf\"", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_tickets_a_rejected_attachment_reaches_the_client_word_for_word_and_is_counted()
    {
        await using var session = await Session.StartAsync(Services(tickets: false, legacy: "false"));
        var c = RefusalCase.Named("placeholder");

        var result = await session.Client.CallToolAsync("submit_order", new Dictionary<string, object?>
        {
            ["request"] = "Fasse die Datei zusammen.",
            ["attachments"] = c.Json,
        });

        Assert.True(result.IsError);
        Assert.Equal(Error("submit_order", c.MessageStart + McpAttachmentRefusalTests.OffWay), Text(result));
        Assert.Equal((RefusalPath.McpInline, c.Code), (Assert.Single(_refusals.Records).Path, _refusals.Records[0].Code));
    }

    [Fact]
    public async Task With_tickets_prepare_attachment_answers_with_the_command_and_the_link()
    {
        await using var session = await Session.StartAsync(Services(tickets: true, legacy: "false"));

        var result = await session.Client.CallToolAsync("prepare_attachment", new Dictionary<string, object?> { ["fileName"] = "Angebot.pdf", ["sizeBytes"] = 210_000 });

        Assert.NotEqual(true, result.IsError);
        using var doc = JsonDocument.Parse(Text(result));
        var root = doc.RootElement;
        var id = root.GetProperty("uploadId").GetString()!;
        Assert.StartsWith("curl --fail-with-body -sS --globoff -T '<path to the file>' -H 'X-Upload-Token: ", root.GetProperty("curlCommand").GetString(), StringComparison.Ordinal);
        Assert.EndsWith($"' https://atelier.example/api/uploads/{id}", root.GetProperty("curlCommand").GetString(), StringComparison.Ordinal);
        Assert.Equal($"https://atelier.example/u/{id}", root.GetProperty("uploadPage").GetString());
        Assert.Equal(25L * 1024 * 1024, root.GetProperty("maxBytes").GetInt64());
        Assert.StartsWith("Run curlCommand with the path of the file in single quotes;", root.GetProperty("next").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_host_starts_with_every_service_of_the_tools_registered()
    {
        await using var provider = Services(tickets: true, legacy: "true").BuildServiceProvider();

        Assert.Contains(Services(tickets: true, legacy: "true"), d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(McpToolBindingCheck));
        await ActivatorUtilities.CreateInstance<McpToolBindingCheck>(provider).StartAsync(CancellationToken.None);
    }

    private ServiceCollection Services(bool tickets, string legacy)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Mcp:LegacyTools"] = legacy }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<UploadOptions>(o => o.Tickets = tickets ? UploadTicketsMode.On : UploadTicketsMode.Off);
        services.Configure<OAuthOptions>(o => o.Issuer = OrderToolTestData.Issuer);
        services.AddSingleton<IOrderService>(_orders);
        services.AddSingleton<ISourceIntakeService>(_intake);
        services.AddSingleton<ISourceRefusalLog>(_refusals);
        services.AddSingleton<IUploadTicketService>(new FakeUploadTickets(tickets));
        services.AddSingleton<ICurrentUserService>(new FakeUser("alice"));
        foreach (var type in typeof(McpAssemblyMarker).Assembly.ToolMethods().SelectMany(m => m.GetParameters()).Select(p => p.ParameterType)
                     .Where(t => t.IsInterface && !t.IsClientValue() && !t.IsSdkValue() && !t.IsGenericType).Distinct())
            services.TryAddScoped(type, _ => throw new NotSupportedException());
        services.AddAtelierMcp();
        return services;
    }

    private static string ToolListJson(IEnumerable<McpClientTool> tools) =>
        string.Join("\n", tools.OrderBy(t => t.Name, StringComparer.Ordinal).Select(t => JsonSerializer.Serialize(t.ProtocolTool, McpJsonUtilities.DefaultOptions)));

    private static IReadOnlyList<string> Properties(Tool tool) =>
        tool.InputSchema.TryGetProperty("properties", out var properties) ? properties.EnumerateObject().Select(p => p.Name).ToList() : [];

    private static IReadOnlyList<string> Required(Tool tool) =>
        tool.InputSchema.TryGetProperty("required", out var required) ? required.EnumerateArray().Select(r => r.GetString()!).ToList() : [];

    private static string Error(string tool, string message) => $"An error occurred invoking '{tool}': {message}";

    private static IReadOnlyList<string> Types(JsonElement schema)
    {
        var type = schema.GetProperty("type");
        return type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().Select(t => t.GetString()!).ToList() : [type.GetString()!];
    }

    private static string Text(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    private sealed class Session(ServiceProvider provider, McpServer server, Task running, McpClient client, CancellationTokenSource stop) : IAsyncDisposable
    {
        public McpClient Client { get; } = client;

        public static async Task<Session> StartAsync(ServiceCollection services)
        {
            var provider = services.BuildServiceProvider();
            var clientToServer = new Pipe();
            var serverToClient = new Pipe();
            var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
            var server = McpServer.Create(
                new StreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream(), "atelier-test", NullLoggerFactory.Instance),
                options, NullLoggerFactory.Instance, provider);
            var stop = new CancellationTokenSource();
            var running = server.RunAsync(stop.Token);
            var client = await McpClient.CreateAsync(
                new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream(), NullLoggerFactory.Instance));
            return new Session(provider, server, running, client, stop);
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await stop.CancelAsync();
            try
            {
                await running;
            }
            catch (OperationCanceledException)
            {
            }

            await server.DisposeAsync();
            await provider.DisposeAsync();
            stop.Dispose();
        }
    }
}
