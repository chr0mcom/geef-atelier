using System.Net;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Tests.EndToEnd.Fakes;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Tests.TestData;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// The whole chain of the web app in one test over the real wiring: upload over <c>POST /api/sources</c> with the
/// cookie client, the order through the real <see cref="IOrderService"/> exactly as the start page submits it, the
/// orchestrator, the direct path with the writer request to the (faked) cli-proxy, and the result over
/// <c>GET /orders/{id}/export/md</c>.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class WebOrderChainEndToEndTests(PostgresFixture postgres)
{
    private const string FirstPageMarker = "CHAIN-MARKER-FIRST-PAGE-5D21";
    private const string LastPageMarker = "CHAIN-MARKER-LAST-PAGE-8B07";
    private const string ResultText = "The council report in short: CHAIN-RESULT-1F4A. The harbour stays open all year.";

    [Fact]
    public async Task Upload_order_direct_path_and_export_work_as_one_chain()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        host.Script.ExecutorText = ResultText;
        var username = "chain-" + Guid.NewGuid().ToString("N")[..6];
        using var client = await host.CreateSignedInClientAsync(username);
        var pdf = SyntheticDocuments.Pdf(80, page => page switch
        {
            1 => $"First page. {FirstPageMarker}",
            80 => $"Last page. {LastPageMarker}",
            _ => $"Routine text of page {page}.",
        });

        var receipt = await host.UploadSourceAsync(client, "harbour-report.pdf", pdf);
        Assert.True(receipt.GetProperty("usable").GetBoolean(), receipt.ToString());
        Assert.Equal("fullText", receipt.GetProperty("budgetClass").GetString());
        var sourceId = receipt.GetProperty("sourceId").GetGuid();

        var submission = await host.SubmitOrderAsync(new SubmitOrderRequest(
            "Summarise the attached report for the council.", username, OrderEntry.Web, [new OrderSourceSelection(sourceId)], OrderMode.Direct));
        var view = await host.WaitForOrderFinishedAsync(submission.OrderId);

        Assert.True(view.Display.State == OrderDisplayState.Delivered, $"{view.Display.State}\n{host.Llm.Describe()}");
        var run = await host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.Id == submission.RunId));
        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal(submission.OrderId, run.OrderId);

        var call = Assert.Single(host.Llm.CallsFor(LlmRole.Executor));
        Assert.True(call.DocumentMode);
        Assert.NotNull(call.ContextDocument);
        Assert.StartsWith("<sources count=\"1\"", call.ContextDocument);
        Assert.Contains(FirstPageMarker, call.ContextDocument);
        Assert.Contains(LastPageMarker, call.ContextDocument);
        Assert.DoesNotContain(FirstPageMarker, call.Messages.Last(m => m.Role == "user").Content);
        var usage = Assert.Single(await host.QueryAsync(db => db.RunSourceUsages.AsNoTracking().ToListAsync()));
        Assert.Equal(sourceId, usage.SourceId);
        Assert.Equal(SourceUsageMode.FullText, usage.Mode);

        using var export = await client.GetAsync($"/orders/{submission.OrderId}/export/md");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("text/markdown", export.Content.Headers.ContentType?.MediaType);
        Assert.Contains("CHAIN-RESULT-1F4A", await export.Content.ReadAsStringAsync());

        using var stranger = await host.CreateSignedInClientAsync(username + "-other");
        using var foreign = await stranger.GetAsync($"/orders/{submission.OrderId}/export/md");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }
}
