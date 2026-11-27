using System.Text;
using System.Text.Json;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Mcp.Tools;
using Geef.Atelier.Tests.EndToEnd.Fakes;
using Geef.Atelier.Tests.Mcp.Orders;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// Orders over the real <c>/mcp</c> endpoint and the real wiring.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class McpOrderEndToEndTests(PostgresFixture postgres)
{
    private const string Request =
        "Write three sentences for new staff that explain when the office closes on Fridays. Use the attached notes.";

    private const string Marker = "Marker-7Q4: the office closes at 14:00 on Fridays.";

    [Fact]
    public async Task Submit_order_with_a_file_waits_with_the_status_tool_and_returns_the_text_with_the_file()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var notes = Encoding.UTF8.GetBytes($"# Office notes\n\n{Marker}\n\nThe reception is staffed until 13:30.\n");

        var submitted = await host.CallMcpToolAsync("submit_order", new
        {
            request = Request,
            attachments = JsonSerializer.Serialize(new[]
            {
                new { filename = "office-notes.md", contentType = "text/markdown", contentBase64 = Convert.ToBase64String(notes) },
            }),
        });
        host.ReleaseOrchestrator();

        var orderId = submitted.GetProperty("orderId").GetString()!;
        Assert.Contains(submitted.GetProperty("state").GetString(), new[] { "queued", "working" });
        Assert.False(string.IsNullOrEmpty(submitted.GetProperty("runId").GetString()));
        var receipt = Assert.Single(submitted.GetProperty("sources").EnumerateArray());
        Assert.Equal("office-notes.md", receipt.GetProperty("fileName").GetString());
        Assert.True(receipt.GetProperty("characters").GetInt32() >= Marker.Length);

        JsonElement status = default;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            status = await host.CallMcpToolAsync("get_order_status", new { orderId, waitSeconds = 50 });
            if (status.GetProperty("done").GetBoolean())
                break;
            Assert.Equal(10, status.GetProperty("nextCheckSeconds").GetInt32());
        }

        Assert.True(status.GetProperty("done").GetBoolean(), $"Order not done: {status}\n{host.Llm.Describe()}");
        Assert.Equal(0, status.GetProperty("nextCheckSeconds").GetInt32());
        Assert.Contains(status.GetProperty("state").GetString(), new[] { "delivered", "delivered_with_open_points" });

        var result = await host.CallMcpToolAsync("get_order_result", new { orderId });
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("text").GetString()), result.ToString());
        var used = Assert.Single(result.GetProperty("sourcesUsed").EnumerateArray());
        Assert.Equal("office-notes.md", used.GetProperty("fileName").GetString());
        Assert.Equal("full_text", used.GetProperty("mode").GetString());

        Assert.True(host.Llm.AnyExecutorPromptContains(Marker), host.Llm.Describe());
        Assert.Contains(host.Llm.CallsFor(LlmRole.Executor), c => c.ContextDocument?.Contains(Marker, StringComparison.Ordinal) == true);
        Assert.DoesNotContain(host.Llm.CallsFor(LlmRole.Executor), c => c.Messages.LastOrDefault(m => m.Role == "user")?.Content?.Contains(Marker, StringComparison.Ordinal) == true);

        await host.WithScopeAsync(async sp =>
        {
            var orders = sp.GetRequiredService<IOrderService>();
            var stranger = new FakeUser("stranger-" + Guid.NewGuid().ToString("N")[..6]);
            var notFound = await Assert.ThrowsAsync<McpException>(() => GetOrderStatusTool.GetOrderStatus(orders, stranger, sp.GetRequiredService<IOptions<OrderOptions>>(), orderId, 0));
            Assert.Equal(McpOrderAccess.OrderNotFound, notFound.Message);
            notFound = await Assert.ThrowsAsync<McpException>(() => GetOrderResultTool.GetOrderResult(orders, stranger, orderId));
            Assert.Equal(McpOrderAccess.OrderNotFound, notFound.Message);
            return 0;
        });

        var order = await host.QueryAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == Guid.Parse(orderId)));
        Assert.Equal(OrderEntry.Mcp, order.Entry);
        Assert.Equal(Request, order.RequestText);
    }

    [Fact]
    public async Task A_placeholder_attachment_is_rejected_with_a_message_the_client_can_read()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => host.CallMcpToolAsync("submit_order", new
        {
            request = Request,
            attachments = JsonSerializer.Serialize(new[]
            {
                new
                {
                    filename = "full-version.pdf",
                    contentType = "application/pdf",
                    contentBase64 = Convert.ToBase64String("Siehe angehängte Vollversion im Briefing-Kontext"u8.ToArray()),
                },
            }),
        }));

        Assert.Contains("full-version.pdf", ex.Message);
        Assert.Contains("not a PDF", ex.Message);
        Assert.Equal(0, await host.QueryAsync(db => db.Orders.CountAsync()));
    }

    [Fact]
    public async Task A_rejected_attachment_reaches_the_client_with_its_message()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => host.CallMcpToolAsync("submit_order", new
        {
            request = "Summarise the file.",
            attachments = JsonSerializer.Serialize(new[]
            {
                new
                {
                    filename = "full-version.md",
                    contentType = "text/markdown",
                    contentBase64 = Convert.ToBase64String("Siehe angehängte Vollversion im Briefing-Kontext"u8.ToArray()),
                },
            }),
        }));

        Assert.True(ex.Message.Contains("full-version.md", StringComparison.Ordinal), ex.Message);
        Assert.Contains("placeholder", ex.Message);
        Assert.Contains("prepare_attachment", ex.Message);
        Assert.Equal(0, await host.QueryAsync(db => db.Orders.CountAsync()));
    }
}
