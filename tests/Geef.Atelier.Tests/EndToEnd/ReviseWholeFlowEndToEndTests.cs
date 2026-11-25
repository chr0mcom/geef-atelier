using System.Globalization;
using System.Text.Json;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// The whole way of a change over the real wiring: an order is written (version 1, with a blocking passage and an open point of the
/// writer), changed over MCP (version 2 keeps both, marked with their version) and changed again on the web (version 3), which could not
/// be finished or was stopped. After each step every place – the order service, "My work", the order page and "My work" rendered by the
/// server, <c>get_order_status</c>, <c>get_order_result</c>, <c>list_orders</c> and the export – shows the same state, the same shown
/// version and the same open points.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class ReviseWholeFlowEndToEndTests(PostgresFixture postgres)
{
    private const string User = AtelierEndToEndHost.McpTokenUser;

    private const string Request = "Schreibe eine kurze Einladung zum Elternabend der Klasse 3b am 12. Mai um 19 Uhr in der Aula, max. 80 Wörter.";

    private const string FirstText = "Liebe Eltern,\n\nder Elternabend der Klasse 3b ist am 12. Mai um 19 Uhr in der Aula.\n\nWir freuen uns auf Sie.";

    private const string SecondText = "Liebe Eltern,\n\nder Elternabend der Klasse 3b ist am 5. Mai um 19 Uhr in der Aula.\n\nWir freuen uns auf Sie.";

    private const string Carried = "Wir freuen uns auf Sie.";

    private const string Point = "Es fehlt, bis wann man sich anmelden soll.";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static string WithNotes(string text, params string[] openPoints) =>
        text + "\n<!-- atelier-notes\n" + JsonSerializer.Serialize(new { assumptions = Array.Empty<string>(), open_points = openPoints, quotes = Array.Empty<object>() }) + "\n-->";

    private static string Unbacked(string quote) => JsonSerializer.Serialize(new
    {
        findings = new[] { new { kind = "unsupported", text_quote = quote, what_is_wrong = "Nobody said that.", instruction_quote = (string?)null, material_quote = (string?)null, material_quote_2 = (string?)null } },
    }, Json);

    private sealed record Seen(
        IReadOnlyList<string> States,
        OrderResult Result,
        JsonElement McpResult,
        int? StatusVersion,
        int? ListVersion,
        int? McpListVersion,
        string? Export,
        string? McpExport,
        string Page);

    private static async Task<Seen> SeeAsync(AtelierEndToEndHost host, Guid orderId)
    {
        var (view, listed, result, export) = await host.WithScopeAsync(async sp =>
        {
            var orders = sp.GetRequiredService<IOrderService>();
            var file = await sp.GetRequiredService<IOrderExportService>().ExportAsync(orderId, ExportFormat.Markdown, null, User);
            return (await orders.GetAsync(orderId, User), (await orders.ListAsync(User)).Single(i => i.OrderId == orderId),
                await orders.GetResultAsync(orderId, User), file is null ? null : System.Text.Encoding.UTF8.GetString(file.Content));
        });
        var id = orderId.ToString();
        var status = await host.CallMcpToolAsync("get_order_status", new { orderId = id, waitSeconds = 0 });
        var mcpResult = await host.CallMcpToolAsync("get_order_result", new { orderId = id });
        var mcpList = await host.CallMcpToolAsync("list_orders", new { limit = 10 });
        var mcpRow = Assert.Single(mcpList.EnumerateArray(), r => r.GetProperty("orderId").GetString() == id);
        var mcpExport = await host.CallMcpToolAsync("export_order", new { orderId = id, format = "md" });

        using var client = await host.CreateSignedInClientAsync(User);
        var page = await client.GetStringAsync($"/orders/{orderId}");
        var work = await client.GetStringAsync("/work");

        return new Seen(
            [
                view!.Display.State.ToWireName(),
                listed.Display.State.ToWireName(),
                result!.Display.State.ToWireName(),
                status.GetProperty("state").GetString()!,
                mcpResult.GetProperty("state").GetString()!,
                mcpRow.GetProperty("state").GetString()!,
                BadgeState(page),
                BadgeState(work),
            ],
            result,
            mcpResult,
            status.TryGetProperty("version", out var sv) ? sv.GetInt32() : null,
            listed.Version,
            mcpRow.TryGetProperty("version", out var lv) ? lv.GetInt32() : null,
            export,
            mcpExport.GetProperty("text").GetString(),
            page);
    }

    private static string BadgeState(string html)
    {
        const string marker = "data-testid=\"order-state\" data-state=\"";
        var at = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(at >= 0, html.Length > 4000 ? html[..4000] : html);
        var start = at + marker.Length;
        return html[start..html.IndexOf('"', start)].Replace('-', '_');
    }

    private static void SameOpenPoints(Seen seen)
    {
        var passage = Assert.Single(seen.Result.ShownPassages());
        Assert.Equal((Carried, (int?)1, true), (passage.Quote, passage.FromVersion, passage.Blocking));
        Assert.Equal([new OrderEarlierPoint(1, Point)], seen.Result.EarlierOpenPoints);
        Assert.Equal(OrderCheckLines.ToReviewOpenPoints, seen.Result.Checks!.ToReview);

        var mcpPassage = Assert.Single(seen.McpResult.GetProperty("unbackedPassages").EnumerateArray());
        Assert.Equal((Carried, 1, true), (mcpPassage.GetProperty("quote").GetString(), mcpPassage.GetProperty("fromVersion").GetInt32(), mcpPassage.GetProperty("blocking").GetBoolean()));
        var mcpEarlier = Assert.Single(seen.McpResult.GetProperty("earlierOpenPoints").EnumerateArray());
        Assert.Equal((1, Point), (mcpEarlier.GetProperty("version").GetInt32(), mcpEarlier.GetProperty("text").GetString()));
        Assert.Equal(OrderCheckLines.ToReviewOpenPoints, seen.McpResult.GetProperty("checks").GetProperty("toReview").GetString());

        Assert.Contains("Found in version 1.", seen.Page, StringComparison.Ordinal);
        Assert.Contains("Noted for version 1", seen.Page, StringComparison.Ordinal);
        Assert.Contains("your change may have settled it:", seen.Page, StringComparison.Ordinal);
        Assert.Contains(Point, seen.Page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Every_place_shows_the_same_state_version_and_open_points_from_version_one_to_a_third_version_that_did_not_finish(bool stopped)
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres, services =>
        {
            services.PostConfigure<DirectOptions>(o =>
            {
                o.FactCheckMode = FactCheckMode.On;
                o.WriterNotes = true;
            });
            services.PostConfigure<OrderOptions>(o => o.Revise = ReviseMode.On);
        });

        host.Script.ExecutorText = WithNotes(FirstText, Point);
        host.Script.EnqueueFactCheckJson(Unbacked(Carried));
        var submission = await host.SubmitOrderAsync(new SubmitOrderRequest(Request, User, OrderEntry.Web, null));
        await host.WaitForOrderFinishedAsync(submission.OrderId);
        var orderId = submission.OrderId;

        host.Script.EnqueueExecutorText(WithNotes(SecondText));
        var revised = await host.CallMcpToolAsync("revise_order", new { orderId = orderId.ToString(), change = "The meeting is on 5 May, not 12 May." });
        Assert.Equal(2, revised.GetProperty("version").GetInt32());
        host.ReleaseOrchestrator();
        await host.WaitForOrderFinishedAsync(orderId);

        var second = await SeeAsync(host, orderId);
        Assert.All(second.States, s => Assert.Equal("delivered_with_open_points", s));
        Assert.Equal((2, 2, SecondText), (second.Result.Version, second.Result.LatestVersion, second.Result.Text));
        Assert.Equal((2, 2, SecondText), (second.McpResult.GetProperty("version").GetInt32(), second.McpResult.GetProperty("latestVersion").GetInt32(), second.McpResult.GetProperty("text").GetString()));
        Assert.Equal(((int?)2, (int?)2, (int?)2), (second.StatusVersion, second.ListVersion, second.McpListVersion));
        Assert.Equal((SecondText, SecondText), (second.Export, second.McpExport));
        SameOpenPoints(second);

        host.Script.EnqueueExecutorError(503, LlmApiError.BackendUnavailableCode);
        var third = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>()
            .ReviseAsync(new ReviseOrderRequest(orderId, User, null, [RevisionQuick.Shorter], OrderEntry.Web)));
        Assert.Equal(3, third.Version);
        host.ReleaseOrchestrator();
        await host.WaitForOrderFinishedAsync(orderId);
        if (stopped)
            await host.QueryAsync(db => db.Runs.Where(r => r.OrderId == orderId && r.OrderVersion == 3).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, RunStatus.Aborted)
                .SetProperty(r => r.Outcome, RunOutcome.Aborted)));

        var seen = await SeeAsync(host, orderId);
        var state = stopped ? "stopped" : "failed";
        Assert.All(seen.States, s => Assert.Equal(state, s));
        var note = string.Format(CultureInfo.InvariantCulture, RevisionLines.NotFinishedFormat, 3, 2);
        Assert.Equal((2, 3, SecondText, note), (seen.Result.Version, seen.Result.LatestVersion, seen.Result.Text, seen.Result.Note));
        Assert.Equal((2, 3, SecondText), (seen.McpResult.GetProperty("version").GetInt32(), seen.McpResult.GetProperty("latestVersion").GetInt32(), seen.McpResult.GetProperty("text").GetString()));
        Assert.StartsWith(note, seen.McpResult.GetProperty("note").GetString(), StringComparison.Ordinal);
        Assert.Equal(((int?)3, (int?)2, (int?)2), (seen.StatusVersion, seen.ListVersion, seen.McpListVersion));
        Assert.Equal((SecondText, SecondText), (seen.Export, seen.McpExport));
        SameOpenPoints(seen);

        Assert.Contains("data-state=\"earlier-version\"", seen.Page, StringComparison.Ordinal);
        Assert.Contains(stopped ? "The change was stopped." : "The change could not be finished.", seen.Page, StringComparison.Ordinal);
        Assert.Contains(note, seen.Page, StringComparison.Ordinal);
        Assert.Contains("ist am 5. Mai", seen.Page, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"order-change-open\"", seen.Page, StringComparison.Ordinal);
        Assert.DoesNotContain("ended without a result", seen.Page, StringComparison.Ordinal);
        Assert.DoesNotContain("No text was written", seen.Page, StringComparison.Ordinal);
    }
}
