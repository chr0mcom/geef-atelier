using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Tests.EndToEnd.Fakes;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>Upload over the cookie client, order submission and the run underneath, over the real wiring.</summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class OrderSubmissionEndToEndTests(PostgresFixture postgres)
{
    [Fact]
    public async Task UploadedFileAndOrderGiveARunWithOrderVersionAndSnapshotFromTheFirstMoment()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var username = "writer-" + Guid.NewGuid().ToString("N")[..6];
        var client = await host.CreateSignedInClientAsync(username);
        var pdf = SyntheticDocuments.Pdf(80, p => p switch
        {
            1 => "MARKER-FIRST-PAGE opens the tender.",
            80 => "MARKER-LAST-PAGE closes the tender.",
            _ => string.Concat(Enumerable.Repeat($"Page {p} describes requirement {p}. ", 8)),
        });

        var receipt = await host.UploadSourceAsync(client, "Tender 8-7102.pdf", pdf);
        Assert.True(receipt.GetProperty("usable").GetBoolean());
        Assert.Equal(80, receipt.GetProperty("pageCount").GetInt32());
        var sourceId = receipt.GetProperty("sourceId").GetGuid();

        var pendingWithoutSnapshot = 0;
        using var watch = new CancellationTokenSource();
        var watcher = Task.Run(async () =>
        {
            while (!watch.IsCancellationRequested)
            {
                pendingWithoutSnapshot += await host.QueryAsync(db => db.Runs.CountAsync(r => r.CreatedByUser == username && r.CrewSnapshot == null));
                await Task.Delay(5);
            }
        });

        var submission = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().SubmitAsync(
            new SubmitOrderRequest("Summarise the tender in five sentences.", username, OrderEntry.Web, [new OrderSourceSelection(sourceId)])));
        var run = await host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.Id == submission.RunId));
        await watch.CancelAsync();
        await watcher;

        Assert.Equal(0, pendingWithoutSnapshot);
        Assert.Equal(submission.OrderId, run.OrderId);
        Assert.Equal(1, run.OrderVersion);
        Assert.Equal(RunStatus.Pending, run.Status);
        Assert.True(CrewSnapshot.IsUsableForRun(run.CrewSnapshot));
        var text = await host.QueryAsync(db => db.Sources.Where(s => s.Id == sourceId).Select(s => s.ExtractedText).SingleAsync());
        Assert.Contains("[[page 1]]\nMARKER-FIRST-PAGE", text, StringComparison.Ordinal);
        Assert.Contains("[[page 80]]\nMARKER-LAST-PAGE", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CarefulOrderRunsToTheEndAndShowsTheTextFirst()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var username = "careful-" + Guid.NewGuid().ToString("N")[..6];

        var submission = await host.SubmitOrderAsync(new SubmitOrderRequest(
            "Write three sentences that explain why the office closes early on Fridays.", username, OrderEntry.Web,
            Mode: OrderMode.Careful, CrewTemplateName: SystemCrew.KlassikTemplateName));
        var view = await host.WaitForOrderFinishedAsync(submission.OrderId);

        Assert.True(view.Display.State == OrderDisplayState.Delivered, $"{view.Display.State}\n{host.Llm.Describe()}");
        Assert.True(view.Display.HasText);
        Assert.All(view.Progress.Steps, s => Assert.Equal(OrderStepStatus.Done, s.Status));
        var result = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetResultAsync(submission.OrderId, username));
        Assert.Equal(ScriptedLlmEndpoint.DefaultExecutorText, result!.Text);
        Assert.True(await host.QueryAsync(db => db.Orders.AnyAsync(o => o.Id == submission.OrderId && o.UpdatedAt >= o.CreatedAt)));
    }
}
