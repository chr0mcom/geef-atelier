using Microsoft.Extensions.Options;
using System.Globalization;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Profiles;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Mcp.Tools;
using Geef.Atelier.Tests.EndToEnd;
using Geef.Atelier.Tests.Mcp.Orders;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;

namespace Geef.Atelier.Tests.Mcp.E2E;

/// <summary>
/// The new order tools of stage 5 over the production wiring (<c>Program</c>, Postgres, orchestrator held, so a new version stays
/// pending): change, the earlier version while the new one waits, export like the web download, list, profile, stop, the tool
/// list, and the separation of users with the real services.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class McpOrderContractEndToEndTests(PostgresFixture postgres)
{
    private const string Request = "Schreib eine kurze Einladung zum Elternabend.";
    private const string V1Text = "Liebe Eltern,\n\nder Elternabend ist am 12. Mai um 19 Uhr.\n\nViele Grüße";
    private const string Change = "Der Elternabend ist am 5. Mai, nicht am 12. Mai.";

    [Fact]
    public async Task Change_export_list_profile_and_stop_work_over_the_real_endpoint()
    {
        await using var host = await StartAsync();
        var (orderId, runId) = await SubmitAndDeliverAsync(host);

        var revised = await host.CallMcpToolAsync("revise_order", new { orderId, change = Change, quick = new[] { "shorter" } });
        Assert.Equal((orderId, "queued", 2), (revised.GetProperty("orderId").GetString(), revised.GetProperty("state").GetString(), revised.GetProperty("version").GetInt32()));
        Assert.Empty(revised.GetProperty("notices").EnumerateArray());
        var v2 = await host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.OrderId == Guid.Parse(orderId) && r.OrderVersion == 2));
        Assert.Equal(Change + " " + RevisionQuick.Shorter.Instruction(), v2.RevisionHint);
        Assert.Equal((runId, V1Text), (v2.ParentRunId!.Value, v2.SeedDraftText));

        var again = await host.CallMcpToolAsync("revise_order", new { orderId, change = "Noch kürzer, bitte." });
        Assert.Equal([RevisionLines.Busy], again.GetProperty("notices").EnumerateArray().Select(n => n.GetString()));
        Assert.Equal(2, again.GetProperty("version").GetInt32());
        Assert.Equal(2, await host.QueryAsync(db => db.Runs.CountAsync(r => r.OrderId == Guid.Parse(orderId))));

        var result = await host.CallMcpToolAsync("get_order_result", new { orderId });
        Assert.Equal((V1Text, 1, 2), (result.GetProperty("text").GetString(), result.GetProperty("version").GetInt32(), result.GetProperty("latestVersion").GetInt32()));
        Assert.StartsWith(string.Format(CultureInfo.InvariantCulture, RevisionLines.StillWritingFormat, 1), result.GetProperty("note").GetString(), StringComparison.Ordinal);

        var status = await host.CallMcpToolAsync("get_order_status", new { orderId, waitSeconds = 0 });
        Assert.Equal((2, false), (status.GetProperty("version").GetInt32(), status.GetProperty("done").GetBoolean()));

        var md = await host.CallMcpToolAsync("export_order", new { orderId, format = "md" });
        var web = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderExportService>()
            .ExportAsync(Guid.Parse(orderId), ExportFormat.Markdown, null, AtelierEndToEndHost.McpTokenUser));
        Assert.Equal(V1Text, md.GetProperty("text").GetString());
        Assert.Equal(web!.Content, Convert.FromBase64String(md.GetProperty("contentBase64").GetString()!));
        Assert.Equal((web.FileName, web.ContentType), (md.GetProperty("fileName").GetString(), md.GetProperty("contentType").GetString()));
        Assert.EndsWith("-v1.md", web.FileName, StringComparison.Ordinal);

        var docx = await host.CallMcpToolAsync("export_order", new { orderId, format = "docx", version = 1 });
        Assert.Equal("PK"u8.ToArray(), Convert.FromBase64String(docx.GetProperty("contentBase64").GetString()!)[..2]);
        Assert.False(docx.TryGetProperty("text", out var docxText) && docxText.ValueKind != System.Text.Json.JsonValueKind.Null);
        var pdf = await host.CallMcpToolAsync("export_order", new { orderId, format = "pdf" });
        Assert.Equal("%PDF"u8.ToArray(), Convert.FromBase64String(pdf.GetProperty("contentBase64").GetString()!)[..4]);
        var missing = await Assert.ThrowsAsync<InvalidOperationException>(() => host.CallMcpToolAsync("export_order", new { orderId, format = "md", version = 2 }));
        Assert.Contains("Version 2 of this order has no text to download.", missing.Message, StringComparison.Ordinal);

        var listed = await host.CallMcpToolAsync("list_orders", new { limit = 10 });
        var row = Assert.Single(listed.EnumerateArray(), r => r.GetProperty("orderId").GetString() == orderId);
        Assert.Equal((1, 3), (row.GetProperty("version").GetInt32(), row.GetProperty("wordCount").GetInt32()));
        Assert.StartsWith("Schreib eine kurze Einladung", row.GetProperty("title").GetString(), StringComparison.Ordinal);
        Assert.True(row.TryGetProperty("updatedAt", out _));

        await host.WithScopeAsync(sp => sp.GetRequiredService<IProfileService>().SaveAsync(AtelierEndToEndHost.McpTokenUser,
            new UserProfileInput("Anna Beispiel", "Elternbeirat", null, Formality.Informal, "de", "Viele Grüße, Anna", null, null)));
        var profile = await host.CallMcpToolAsync("profile", new { });
        Assert.Equal(("Anna Beispiel", "informal", "de", false), (profile.GetProperty("name").GetString(), profile.GetProperty("formality").GetString(),
            profile.GetProperty("language").GetString(), profile.GetProperty("isEmpty").GetBoolean()));

        var stopped = await host.CallMcpToolAsync("cancel_order", new { orderId });
        Assert.True(stopped.GetProperty("changed").GetBoolean());
        Assert.True(await host.QueryAsync(db => db.Runs.Where(r => r.Id == v2.Id).Select(r => r.CancellationRequested).SingleAsync()));
        var exportAfterStop = await host.CallMcpToolAsync("export_order", new { orderId });
        Assert.Equal(V1Text, exportAfterStop.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Tools_list_as_shipped_has_the_ten_order_tools_and_the_older_ones_with_a_pointer()
    {
        await using var host = await StartAsync();

        var tools = (await host.PostMcpAsync("tools/list")).GetProperty("result").GetProperty("tools").EnumerateArray()
            .ToDictionary(t => t.GetProperty("name").GetString()!, t => t.GetProperty("description").GetString()!);

        Assert.Equal(29, tools.Count);
        foreach (var name in new[] { "prepare_attachment", "submit_order", "answer_order", "get_order_status", "get_order_result", "revise_order", "export_order", "cancel_order", "list_orders", "profile" })
            Assert.Contains(name, tools.Keys);
        Assert.StartsWith("Older tool, kept for now: use list_orders instead. ", tools["list_runs"], StringComparison.Ordinal);
        Assert.Contains("max 50", tools["get_order_status"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_older_tools_only_the_ten_are_listed_and_the_instructions_do_not_name_older_ones()
    {
        await using var host = await StartAsync(services => services.PostConfigure<McpOptions>(o => o.LegacyTools = false));

        var tools = (await host.PostMcpAsync("tools/list")).GetProperty("result").GetProperty("tools").EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()!).Order(StringComparer.Ordinal).ToList();
        var instructions = (await host.PostMcpAsync("initialize", new
        {
            protocolVersion = "2025-06-18",
            capabilities = new { },
            clientInfo = new { name = "e2e-client", version = "1.0" },
        })).GetProperty("result").GetProperty("instructions").GetString()!;

        Assert.Equal(new[] { "answer_order", "cancel_order", "export_order", "get_order_result", "get_order_status", "list_orders", "prepare_attachment", "profile", "revise_order", "submit_order" }, tools);
        Assert.DoesNotContain("submit_request", instructions, StringComparison.Ordinal);
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => host.CallMcpToolAsync("list_runs", new { }));
        Assert.Contains("list_runs", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revise_order_says_plainly_when_changing_is_switched_off()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres, services =>
            services.PostConfigure<OrderOptions>(o => o.Revise = ReviseMode.Off));
        var (orderId, _) = await SubmitAndDeliverAsync(host);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => host.CallMcpToolAsync("revise_order", new { orderId, change = Change }));

        Assert.Contains(RevisionLines.NotAvailable, refused.Message, StringComparison.Ordinal);
        Assert.Equal(1, await host.QueryAsync(db => db.Runs.CountAsync(r => r.OrderId == Guid.Parse(orderId))));
    }

    [Fact]
    public async Task Another_person_neither_sees_nor_changes_nor_stops_nor_downloads_an_order_and_never_reads_its_owners_profile()
    {
        await using var host = await StartAsync();
        const string owner = "e2e-owner";
        var submission = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().SubmitAsync(new SubmitOrderRequest(Request, owner, OrderEntry.Mcp)));
        await DeliverAsync(host, submission.RunId!.Value, V1Text);
        await host.WithScopeAsync(sp => sp.GetRequiredService<IProfileService>().SaveAsync(owner,
            new UserProfileInput("Olga Owner", null, null, Formality.Formal, "de", null, null, null)));
        var id = submission.OrderId.ToString();
        var mallory = new FakeUser("e2e-mallory");

        await host.WithScopeAsync(async sp =>
        {
            var orders = sp.GetRequiredService<IOrderService>();
            var exports = sp.GetRequiredService<IOrderExportService>();
            foreach (var call in new Func<Task>[]
                     {
                         () => ReviseOrderTool.ReviseOrder(orders, mallory, id, Change),
                         () => ExportOrderTool.ExportOrder(orders, exports, mallory, id, "md"),
                         () => ExportOrderTool.ExportOrder(orders, exports, mallory, id, "html", 7),
                         () => CancelOrderTool.CancelOrder(orders, mallory, id),
                         () => GetOrderResultTool.GetOrderResult(orders, mallory, id),
                     })
                Assert.Equal("Order not found.", (await Assert.ThrowsAsync<McpException>(call)).Message);

            Assert.DoesNotContain(await ListOrdersTool.ListOrders(orders, mallory), r => r.OrderId == id);
            var profile = await ProfileTool.Profile(sp.GetRequiredService<IProfileService>(), mallory, sp.GetRequiredService<IOptions<OrderOptions>>());
            Assert.True(profile.IsEmpty);
            Assert.Null(profile.Name);

            var own = await ExportOrderTool.ExportOrder(orders, exports, new FakeUser(owner), id, "md");
            Assert.Equal(V1Text, own.Text);
            Assert.Contains(await ListOrdersTool.ListOrders(orders, new FakeUser(owner)), r => r.OrderId == id);
            return 0;
        });

        var runs = await host.QueryAsync(db => db.Runs.AsNoTracking().Where(r => r.OrderId == submission.OrderId).ToListAsync());
        var run = Assert.Single(runs);
        Assert.False(run.CancellationRequested);
        Assert.Equal(RunStatus.Completed, run.Status);
    }

    private Task<AtelierEndToEndHost> StartAsync(Action<IServiceCollection>? configure = null) =>
        AtelierEndToEndHost.StartAsync(postgres, services =>
        {
            services.PostConfigure<OrderOptions>(o => o.Revise = ReviseMode.On);
            configure?.Invoke(services);
        });

    private static async Task<(string OrderId, Guid RunId)> SubmitAndDeliverAsync(AtelierEndToEndHost host)
    {
        var submitted = await host.CallMcpToolAsync("submit_order", new { request = Request });
        var runId = Guid.Parse(submitted.GetProperty("runId").GetString()!);
        await DeliverAsync(host, runId, V1Text);
        return (submitted.GetProperty("orderId").GetString()!, runId);
    }

    private static Task DeliverAsync(AtelierEndToEndHost host, Guid runId, string text) =>
        host.QueryAsync(db => db.Runs.Where(r => r.Id == runId).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, RunStatus.Completed)
            .SetProperty(r => r.Outcome, RunOutcome.Converged)
            .SetProperty(r => r.FinalText, text)
            .SetProperty(r => r.WordCount, 3)
            .SetProperty(r => r.CompletedAt, DateTimeOffset.UtcNow.AddMinutes(-10))));
}
