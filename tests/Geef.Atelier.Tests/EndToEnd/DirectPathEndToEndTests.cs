using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Tests.EndToEnd.Fakes;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// The direct path over the real wiring: upload, order, orchestrator, writer request to the (faked) cli-proxy, checks,
/// report.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class DirectPathEndToEndTests(PostgresFixture postgres)
{
    private const string FirstPageMarker = "E2E-MARKER-FIRST-PAGE-3A71";
    private const string LastPageMarker = "E2E-MARKER-LAST-PAGE-C09D";

    private static string Words(int count) => string.Join(' ', Enumerable.Repeat("Hallo", count));

    private static async Task<(AtelierEndToEndHost Host, Guid OrderId)> SubmitAsync(
        PostgresFixture postgres, string request, Action<ScriptedLlmEndpoint> script, bool withPdf = false, OrderMode mode = OrderMode.Direct)
    {
        var host = await AtelierEndToEndHost.StartAsync(postgres);
        script(host.Script);
        var sources = new List<OrderSourceSelection>();
        if (withPdf)
        {
            using var client = await host.CreateSignedInClientAsync("e2e-direct");
            var pdf = SyntheticDocuments.Pdf(80, page => page switch
            {
                1 => $"First page. {FirstPageMarker}",
                80 => $"Last page. {LastPageMarker}",
                _ => $"Routine text of page {page}.",
            });
            var receipt = await host.UploadSourceAsync(client, "harbour-report.pdf", pdf);
            sources.Add(new OrderSourceSelection(receipt.GetProperty("sourceId").GetGuid()));
        }

        var submission = await host.SubmitOrderAsync(new SubmitOrderRequest(request, "e2e-direct", OrderEntry.Web, sources, mode));
        await host.WaitForOrderFinishedAsync(submission.OrderId);
        return (host, submission.OrderId);
    }

    private static Task<RunEntity> RunOfAsync(AtelierEndToEndHost host, Guid orderId) =>
        host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.OrderId == orderId && r.Kind == RunKind.Standard));

    [Fact]
    public async Task Markers_of_the_first_and_the_last_page_reach_the_writer_in_document_mode()
    {
        var (host, orderId) = await SubmitAsync(postgres, "Summarise the attached report for the council.", s => s.ExecutorText = Words(100), withPdf: true);
        await using var lifetime = host;

        var call = Assert.Single(host.Llm.CallsFor(LlmRole.Executor));
        Assert.True(call.DocumentMode);
        Assert.Contains(FirstPageMarker, call.ContextDocument);
        Assert.Contains(LastPageMarker, call.ContextDocument);
        Assert.Contains("[[page 1]]", call.ContextDocument);
        Assert.Contains("[[page 80]]", call.ContextDocument);
        Assert.Contains("Summarise the attached report", call.Messages.Last(m => m.Role == "user").Content);
        var usage = Assert.Single(await host.QueryAsync(db => db.RunSourceUsages.AsNoTracking().ToListAsync()));
        Assert.Equal(SourceUsageMode.FullText, usage.Mode);
        Assert.Equal((await RunOfAsync(host, orderId)).Id, usage.RunId);
    }

    [Fact]
    public async Task A_clean_text_converges_after_one_pass_with_a_report()
    {
        var (host, orderId) = await SubmitAsync(postgres, "Schreibe eine kurze Notiz an das Team, max. 50 Wörter.", s => s.ExecutorText = Words(40));
        await using var lifetime = host;

        var run = await RunOfAsync(host, orderId);
        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal((RunOutcome?)RunOutcome.Converged, run.Outcome);
        var report = RunReportJson.TryParse(run.ReportJson);
        Assert.NotNull(report);
        Assert.True(Assert.Single(report.Checks, c => c.Code == DraftCheckCodes.Length).Passed);
    }

    [Fact]
    public async Task A_length_violation_is_corrected_once()
    {
        var (host, orderId) = await SubmitAsync(postgres, "Schreibe eine kurze Notiz an das Team, max. 50 Wörter.", s =>
        {
            s.EnqueueExecutorText(Words(80));
            s.EnqueueExecutorText(Words(45));
        });
        await using var lifetime = host;

        var writer = host.Llm.CallsFor(LlmRole.Executor);
        Assert.Equal(2, writer.Count);
        Assert.Contains("1. The text has 80 words.", writer[1].Messages.Last(m => m.Role == "user").Content);
        var run = await RunOfAsync(host, orderId);
        Assert.Equal((RunOutcome?)RunOutcome.Converged, run.Outcome);
        Assert.Equal(2, run.DeliveredIteration);
    }

    [Fact]
    public async Task Two_violations_deliver_with_open_points()
    {
        var (host, orderId) = await SubmitAsync(postgres, "Schreibe eine kurze Notiz an das Team, max. 50 Wörter.", s => s.ExecutorText = Words(80));
        await using var lifetime = host;

        var run = await RunOfAsync(host, orderId);
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal((RunOutcome?)RunOutcome.DeliveredWithOpenPoints, run.Outcome);
        Assert.Single(RunReportJson.TryParse(run.ReportJson)!.OpenChecks);
        var order = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetAsync(orderId, null));
        Assert.Equal(OrderDisplayState.DeliveredWithOpenPoints, order!.Display.State);
        var result = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetResultAsync(orderId, "e2e-direct"));
        Assert.False(string.IsNullOrWhiteSpace(result!.Text));
        Assert.Equal(DraftCheckCodes.Length, Assert.Single(result.OpenChecks).Code);
    }

    [Fact]
    public async Task Careful_orders_pass_the_file_to_the_executor()
    {
        var (host, _) = await SubmitAsync(postgres, "Summarise the attached report carefully.", s => s.ExecutorText = Words(100), withPdf: true, mode: OrderMode.Careful);
        await using var lifetime = host;

        Assert.True(host.Llm.AnyExecutorPromptContains(FirstPageMarker), host.Llm.Describe());
    }

    [Fact]
    public async Task The_direct_path_never_asks_a_model_for_a_review()
    {
        var (host, _) = await SubmitAsync(postgres, "Schreibe eine kurze Notiz an das Team.", s => s.ExecutorText = Words(40));
        await using var lifetime = host;

        Assert.Empty(host.Llm.CallsFor(LlmRole.Reviewer));
    }

    [Fact]
    public async Task An_empty_answer_twice_fails_the_run_with_a_plain_message()
    {
        var (host, orderId) = await SubmitAsync(postgres, "Schreibe eine kurze Notiz an das Team.", s =>
        {
            s.EnqueueExecutorError(502, "empty_result");
            s.EnqueueExecutorText("");
        });
        await using var lifetime = host;

        var run = await RunOfAsync(host, orderId);
        Assert.True(run.Status == RunStatus.Failed, $"Status {run.Status}.\n{host.Llm.Describe()}");
        Assert.Equal("The writer returned no text. Please try again.", run.ErrorMessage);
        Assert.Equal(2, host.Llm.CallsFor(LlmRole.Executor).Count);
    }

    [Fact]
    public async Task An_unavailable_writing_service_fails_the_run_after_one_call()
    {
        var (host, orderId) = await SubmitAsync(postgres, "Schreibe eine kurze Notiz an das Team.", s =>
            s.EnqueueExecutorError(503, "backend_unavailable"));
        await using var lifetime = host;

        var run = await RunOfAsync(host, orderId);
        Assert.True(run.Status == RunStatus.Failed, $"Status {run.Status}.\n{host.Llm.Describe()}");
        Assert.Equal("The writing service is busy. Please try again in a few minutes.", run.ErrorMessage);
        Assert.Single(host.Llm.CallsFor(LlmRole.Executor));
    }
}
