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

/// <summary>The legacy path (<see cref="IRunService"/>) links its runs to orders; the orchestrator stays held.</summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class RunServiceOrderLinkTests(PostgresFixture postgres)
{
    [Fact]
    public async Task NewRunsStartCarefulOrdersTaskRunsInheritResumesTakeTheNextVersionLearningStaysAlone()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var user = NewUser();

        var plain = await SubmitAsync(host, new SubmitRunRequest("Write a short poem.", "{}", user, SystemCrew.KlassikTemplateName));
        var plainRun = await RunAsync(host, plain);
        Assert.Equal(plain, plainRun.OrderId);
        Assert.Equal(1, plainRun.OrderVersion);
        var plainOrder = await host.QueryAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == plain));
        Assert.Equal((OrderMode.Careful, OrderState.Ready, OrderEntry.Web, "Write a short poem.", SystemCrew.KlassikTemplateName),
            (plainOrder.Mode, plainOrder.State, plainOrder.Entry, plainOrder.RequestText, plainOrder.CrewTemplateName));

        var composition = await SubmitAsync(host, new SubmitRunRequest("Plan the launch event.", "{}", user, AutoCompose: true, Entry: OrderEntry.Mcp));
        var compositionOrder = await host.QueryAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == composition));
        Assert.Equal("Plan the launch event.", compositionOrder.RequestText);
        Assert.Equal(OrderEntry.Mcp, compositionOrder.Entry);

        var task = await SubmitAsync(host, new SubmitRunRequest("Plan the launch event.", "{}", user, SystemCrew.KlassikTemplateName, ParentCompositionRunId: composition));
        var taskRun = await RunAsync(host, task);
        Assert.Equal((composition, 1), (taskRun.OrderId!.Value, taskRun.OrderVersion!.Value));

        await host.QueryAsync(db => db.Runs.Where(r => r.Id == task)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RunStatus.Failed).SetProperty(r => r.Outcome, RunOutcome.Failed)));
        var resumed = await host.WithScopeAsync(sp => sp.GetRequiredService<IRunService>().ResumeRunAsync(new ResumeOptions(task, false, null), user));
        var resumedRun = await RunAsync(host, resumed);
        Assert.Equal((composition, 2), (resumedRun.OrderId!.Value, resumedRun.OrderVersion!.Value));

        var learning = await SubmitAsync(host, new SubmitRunRequest("Evaluate.", "{}", user, SystemCrew.KlassikTemplateName, Kind: RunKind.Learning));
        Assert.Null((await RunAsync(host, learning)).OrderId);

        Assert.Equal(2, await host.QueryAsync(db => db.Orders.CountAsync(o => o.CreatedByUser == user)));
    }

    [Fact]
    public async Task DeletingTheLastRunOfAnOrderDeletesTheOrder()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var user = NewUser();
        var first = await SubmitAsync(host, new SubmitRunRequest("Write a slogan.", "{}", user, SystemCrew.KlassikTemplateName));
        await host.QueryAsync(db => db.Runs.Where(r => r.Id == first)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RunStatus.Failed).SetProperty(r => r.Outcome, RunOutcome.Failed)));
        var second = await host.WithScopeAsync(sp => sp.GetRequiredService<IRunService>().ResumeRunAsync(new ResumeOptions(first, false, null), user));
        await host.QueryAsync(db => db.Runs.Where(r => r.Id == second)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RunStatus.Completed).SetProperty(r => r.Outcome, RunOutcome.Converged)));

        await host.WithScopeAsync(sp => sp.GetRequiredService<IRunService>().DeleteRunAsync(second, user));
        Assert.True(await host.QueryAsync(db => db.Orders.AnyAsync(o => o.Id == first)));

        await host.WithScopeAsync(sp => sp.GetRequiredService<IRunService>().DeleteRunAsync(first, user));
        Assert.False(await host.QueryAsync(db => db.Orders.AnyAsync(o => o.Id == first)));
    }

    private static Task<Guid> SubmitAsync(AtelierEndToEndHost host, SubmitRunRequest request) =>
        host.WithScopeAsync(sp => sp.GetRequiredService<IRunService>().SubmitRunAsync(request));

    private static Task<RunEntity> RunAsync(AtelierEndToEndHost host, Guid runId) =>
        host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.Id == runId));
}
