using System.Text.Json;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// A new version that keeps a blocking passage of the version before open is "Ready – please check" everywhere, over the real wiring:
/// the order page and "My work" (rendered by the server), the order service and the MCP tools <c>get_order_status</c> and
/// <c>get_order_result</c>. The rule only applies while the fact check blocks; in report mode an own blocking passage shows "Ready"
/// and so does a carried one. A stored report without <c>fromVersion</c> (written before this rule) stays as it was.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class ReviseCarriedPointEndToEndTests(PostgresFixture postgres)
{
    private const string User = AtelierEndToEndHost.McpTokenUser;

    private const string Request = "Schreibe eine kurze Einladung zum Elternabend der Klasse 3b am 12. Mai um 19 Uhr in der Aula, max. 80 Wörter.";

    private const string FirstText = "Liebe Eltern,\n\nder Elternabend der Klasse 3b ist am 12. Mai um 19 Uhr in der Aula.\n\nWir freuen uns auf Sie.";

    private const string SecondText = "Liebe Eltern,\n\nder Elternabend der Klasse 3b ist am 5. Mai um 19 Uhr in der Aula.\n\nWir freuen uns auf Sie.";

    private const string Carried = "Wir freuen uns auf Sie.";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static Task<AtelierEndToEndHost> StartAsync(PostgresFixture postgres, FactCheckMode mode) =>
        AtelierEndToEndHost.StartAsync(postgres, services =>
        {
            services.PostConfigure<DirectOptions>(o => o.FactCheckMode = mode);
            services.PostConfigure<OrderOptions>(o => o.Revise = ReviseMode.On);
        });

    private static string Unbacked(string quote) => JsonSerializer.Serialize(new
    {
        findings = new[] { new { kind = "unsupported", text_quote = quote, what_is_wrong = "Nobody said that.", instruction_quote = (string?)null, material_quote = (string?)null, material_quote_2 = (string?)null } },
    }, Json);

    private static async Task<Guid> TwoVersionsAsync(AtelierEndToEndHost host)
    {
        host.Script.ExecutorText = FirstText;
        host.Script.EnqueueFactCheckJson(Unbacked(Carried));
        var submission = await host.SubmitOrderAsync(new SubmitOrderRequest(Request, User, OrderEntry.Web, null));
        await host.WaitForOrderFinishedAsync(submission.OrderId);

        host.Script.EnqueueExecutorText(SecondText);
        var revision = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>()
            .ReviseAsync(new ReviseOrderRequest(submission.OrderId, User, "The meeting is on 5 May, not 12 May.", [], OrderEntry.Web)));
        Assert.Equal(2, revision.Version);
        host.ReleaseOrchestrator();
        await host.WaitForOrderFinishedAsync(submission.OrderId);
        return submission.OrderId;
    }

    private static Task<RunEntity> RunAsync(AtelierEndToEndHost host, Guid orderId, int version) =>
        host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.OrderId == orderId && r.OrderVersion == version));

    private static async Task<IReadOnlyList<string>> ShownStatesAsync(AtelierEndToEndHost host, Guid orderId)
    {
        var (view, listed, result) = await host.WithScopeAsync(async sp =>
        {
            var orders = sp.GetRequiredService<IOrderService>();
            return (await orders.GetAsync(orderId, User), (await orders.ListAsync(User)).Single(i => i.OrderId == orderId), await orders.GetResultAsync(orderId, User));
        });
        var status = await host.CallMcpToolAsync("get_order_status", new { orderId = orderId.ToString() });
        var mcpResult = await host.CallMcpToolAsync("get_order_result", new { orderId = orderId.ToString() });

        using var client = await host.CreateSignedInClientAsync(User);
        var page = await client.GetStringAsync($"/orders/{orderId}");
        var work = await client.GetStringAsync("/work");

        return
        [
            view!.Display.State.ToWireName(),
            listed.Display.State.ToWireName(),
            result!.Display.State.ToWireName(),
            status.GetProperty("state").GetString()!,
            mcpResult.GetProperty("state").GetString()!,
            BadgeState(page),
            BadgeState(work),
        ];
    }

    private static string BadgeState(string html)
    {
        const string marker = "data-testid=\"order-state\" data-state=\"";
        var at = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(at >= 0, html.Length > 4000 ? html[..4000] : html);
        var start = at + marker.Length;
        return html[start..html.IndexOf('"', start)].Replace('-', '_');
    }

    [Fact]
    public async Task A_blocking_passage_kept_from_version_one_shows_version_two_as_ready_to_check_everywhere()
    {
        await using var host = await StartAsync(postgres, FactCheckMode.On);

        var orderId = await TwoVersionsAsync(host);

        var first = await RunAsync(host, orderId, 1);
        Assert.Equal(RunOutcome.DeliveredWithOpenPoints, first.Outcome);
        var second = await RunAsync(host, orderId, 2);
        Assert.Equal((RunStatus.Completed, RunOutcome.Converged, SecondText), (second.Status, second.Outcome, second.FinalText));
        var kept = Assert.Single(RunReportJson.TryParse(second.ReportJson)!.FactCheck!.Open);
        Assert.Equal((Carried, (int?)1, true), (kept.TextQuote, kept.FromVersion, kept.Blocking));

        var states = await ShownStatesAsync(host, orderId);

        Assert.All(states, s => Assert.Equal("delivered_with_open_points", s));
        var result = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetResultAsync(orderId, User));
        Assert.Equal(2, result!.Version);
        Assert.Equal("The text is ready, but some checks did not pass. Please check it before you use it.", result.Note);
    }

    [Fact]
    public async Task A_stored_report_without_the_version_field_shows_ready_as_before()
    {
        await using var host = await StartAsync(postgres, FactCheckMode.On);
        var orderId = await TwoVersionsAsync(host);
        var second = await RunAsync(host, orderId, 2);
        var report = RunReportJson.TryParse(second.ReportJson)!;
        var older = RunReportJson.Serialize(report with
        {
            FactCheck = report.FactCheck! with { Open = report.FactCheck.Open.Select(f => f with { FromVersion = null }).ToList() },
        });
        Assert.DoesNotContain("fromVersion", older, StringComparison.Ordinal);
        await host.QueryAsync(db => db.Runs.Where(r => r.Id == second.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.ReportJson, older)));

        var states = await ShownStatesAsync(host, orderId);

        Assert.All(states, s => Assert.Equal("delivered", s));
    }

    [Fact]
    public async Task In_report_mode_a_kept_passage_shows_ready_like_an_own_one()
    {
        await using var host = await StartAsync(postgres, FactCheckMode.Report);

        var orderId = await TwoVersionsAsync(host);

        Assert.Equal(RunOutcome.Converged, (await RunAsync(host, orderId, 1)).Outcome);
        var kept = Assert.Single(RunReportJson.TryParse((await RunAsync(host, orderId, 2)).ReportJson)!.FactCheck!.Open);
        Assert.Equal((int?)1, kept.FromVersion);
        var states = await ShownStatesAsync(host, orderId);
        Assert.All(states, s => Assert.Equal("delivered", s));
    }
}
