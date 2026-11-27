using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Tests.EndToEnd;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Orders;

/// <summary>A resumed run (<see cref="IRunService.ResumeRunAsync"/>) joins the order of its parent; deleting runs deletes an emptied order. The orchestrator stays held.</summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class RunServiceOrderLinkTests(PostgresFixture postgres)
{
    [Fact]
    public async Task AResumedRunJoinsTheOrderOfItsParentAsTheNextVersion()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var user = NewUser();
        var first = await FailedFirstRunAsync(host, user, "Write a short poem.");

        var resumed = await host.WithScopeAsync(sp => sp.GetRequiredService<IRunService>().ResumeRunAsync(new ResumeOptions(first.RunId, false, null), user));

        var resumedRun = await RunAsync(host, resumed);
        Assert.Equal((first.OrderId, 2), (resumedRun.OrderId!.Value, resumedRun.OrderVersion!.Value));
        Assert.Equal(SystemCrew.DirectTemplateName, resumedRun.CrewTemplateName);
        Assert.Equal(1, await host.QueryAsync(db => db.Orders.CountAsync(o => o.CreatedByUser == user)));
    }

    [Fact]
    public async Task DeletingTheLastRunOfAnOrderDeletesTheOrder()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var user = NewUser();
        var first = await FailedFirstRunAsync(host, user, "Write a slogan.");
        var second = await host.WithScopeAsync(sp => sp.GetRequiredService<IRunService>().ResumeRunAsync(new ResumeOptions(first.RunId, false, null), user));
        await host.QueryAsync(db => db.Runs.Where(r => r.Id == second)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RunStatus.Completed).SetProperty(r => r.Outcome, RunOutcome.Converged)));

        await host.WithScopeAsync(sp => sp.GetRequiredService<IRunService>().DeleteRunAsync(second, user));
        Assert.True(await host.QueryAsync(db => db.Orders.AnyAsync(o => o.Id == first.OrderId)));

        await host.WithScopeAsync(sp => sp.GetRequiredService<IRunService>().DeleteRunAsync(first.RunId, user));
        Assert.False(await host.QueryAsync(db => db.Orders.AnyAsync(o => o.Id == first.OrderId)));
    }

    private static async Task<(Guid OrderId, Guid RunId)> FailedFirstRunAsync(AtelierEndToEndHost host, string user, string request)
    {
        var submission = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>()
            .SubmitAsync(new SubmitOrderRequest(request, user, OrderEntry.Web, AllowQuestions: false)));
        var runId = submission.RunId!.Value;
        await host.QueryAsync(db => db.Runs.Where(r => r.Id == runId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RunStatus.Failed).SetProperty(r => r.Outcome, RunOutcome.Failed)));
        return (submission.OrderId, runId);
    }

    private static Task<RunEntity> RunAsync(AtelierEndToEndHost host, Guid runId) =>
        host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.Id == runId));
}
