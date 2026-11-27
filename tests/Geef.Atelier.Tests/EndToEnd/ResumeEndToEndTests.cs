using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>"Resume" of a failed first run over the real wiring: the new run is stored at once as the next version of the same order.</summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class ResumeEndToEndTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_resumed_failed_run_becomes_the_next_version_of_its_order_and_delivers()
    {
        const string user = "e2e-resume";
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        host.Script.EnqueueExecutorError(503, LlmApiError.BackendUnavailableCode);
        var submission = await host.SubmitOrderAsync(new SubmitOrderRequest("Write a short note about the open room.", user, OrderEntry.Web, null));
        await host.WaitForOrderFinishedAsync(submission.OrderId);
        var failed = await host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.OrderId == submission.OrderId));
        Assert.Equal(RunStatus.Failed, failed.Status);

        host.Script.EnqueueExecutorText("The room is open from Tuesday at ten.");
        var resumedId = await host.WithScopeAsync(sp => sp.GetRequiredService<IRunService>()
            .ResumeRunAsync(new ResumeOptions(failed.Id, UseSeedDraft: false, MaxIterationsOverride: null), user));
        await host.WaitForTerminalAsync(resumedId);
        var view = await host.WaitForOrderFinishedAsync(submission.OrderId);

        var runs = await host.QueryAsync(db => db.Runs.AsNoTracking().Where(r => r.OrderId == submission.OrderId).OrderBy(r => r.OrderVersion).ToListAsync());
        Assert.Equal([1, 2], runs.Select(r => r.OrderVersion!.Value));
        Assert.Equal((resumedId, failed.Id, RunStatus.Completed), (runs[1].Id, runs[1].ParentRunId!.Value, runs[1].Status));
        Assert.True(view.Display.HasText);
        Assert.Equal(1, await host.QueryAsync(db => db.Orders.CountAsync(o => o.CreatedByUser == user)));
    }
}
