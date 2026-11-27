using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Infrastructure.Pipeline.Assembly;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// Runs and orders that older versions stored (stage 8d, D-091): the old review loop with its snapshot, a composed team and a learning
/// run. The database keeps them, so the start, the lists, "My work", the pages and the orchestrator with its crash recovery must cope:
/// finished ones stay readable, pending ones fail with a plain sentence instead of running as something else.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class LegacyDataEndToEndTests(PostgresFixture postgres)
{
    private const string OldText = "# Old text\n\nWritten by the removed review loop.";

    [Fact(Timeout = 90_000)]
    public async Task Finished_old_orders_stay_readable_in_lists_my_work_and_on_their_pages()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var user = NewUser();
        var (careful, carefulRun) = await StoreFinishedOldOrderAsync(host, user, OrderMode.Careful, RunKind.Standard);
        var (composed, composedRun) = await StoreComposedOldOrderAsync(host, user);

        await using var scope = host.Services.CreateAsyncScope();
        var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();
        var listed = await orders.ListAsync(user);
        var result = await orders.GetResultAsync(careful, user);
        var view = await orders.GetAsync(composed, user);
        var runView = await scope.ServiceProvider.GetRequiredService<IRunService>().GetRunViewAsync(carefulRun, user);

        Assert.Contains(listed, o => o.OrderId == careful);
        Assert.Contains(listed, o => o.OrderId == composed);
        Assert.Contains("Written by the removed review loop.", result!.Text);
        Assert.NotNull(view);
        Assert.Equal("klassik", runView!.Snapshot!.TemplateName);

        using var client = await host.CreateSignedInClientAsync(user);
        foreach (var path in new[] { "/work", "/orders", "/runs", $"/orders/{careful}", $"/orders/{composed}", $"/runs/{carefulRun}", $"/runs/{composedRun}" })
        {
            using var response = await client.GetAsync(path);
            Assert.True(response.IsSuccessStatusCode, $"{path}: {(int)response.StatusCode}");
        }
        var runPage = await client.GetStringAsync($"/runs/{carefulRun}");
        Assert.Contains("Setup: klassik", runPage, StringComparison.Ordinal);
    }

    [Fact(Timeout = 90_000)]
    public async Task Pending_old_runs_fail_with_a_plain_sentence_and_running_ones_are_recovered()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var user = NewUser();
        var oldLoop = await StorePendingOldRunAsync(host, user, RunKind.Standard, withOrder: true);
        var composition = await StorePendingOldRunAsync(host, user, RunKind.CrewComposition, withOrder: true);
        var learning = await StorePendingOldRunAsync(host, user, RunKind.Learning, withOrder: false);
        var interrupted = await StoreRunningOldRunAsync(host, user);

        host.ReleaseOrchestrator();

        foreach (var runId in new[] { oldLoop, composition, learning })
        {
            var run = await host.WaitForTerminalAsync(runId, TimeSpan.FromSeconds(30));
            Assert.Equal(RunStatus.Failed, run.Status);
            Assert.Equal((RunOutcome?)RunOutcome.Failed, run.Outcome);
            Assert.Equal(PipelineAssemblerSelector.RetiredSetupMessage, run.ErrorMessage);
        }

        var recovered = await host.WaitForTerminalAsync(interrupted, TimeSpan.FromSeconds(30));
        Assert.Equal(RunStatus.Failed, recovered.Status);
        Assert.Equal((RunOutcome?)RunOutcome.Failed, recovered.Outcome);

        await using var scope = host.Services.CreateAsyncScope();
        Assert.NotEmpty(await scope.ServiceProvider.GetRequiredService<IOrderService>().ListAsync(user));
    }

    private static async Task<(Guid Order, Guid Run)> StoreFinishedOldOrderAsync(AtelierEndToEndHost host, string user, OrderMode mode, RunKind kind)
    {
        var order = Order(user, "Write the old text.") with { Mode = mode, CrewTemplateName = "klassik" };
        var run = OldRun(user, order.Id, kind, RunStatus.Completed, RunOutcome.Converged, OldText);
        await host.QueryAsync(async db =>
        {
            db.Orders.Add(order);
            db.Runs.Add(run);
            return await db.SaveChangesAsync();
        });
        return (order.Id, run.Id);
    }

    private static async Task<(Guid Order, Guid Run)> StoreComposedOldOrderAsync(AtelierEndToEndHost host, string user)
    {
        var order = Order(user, "Write the composed text.") with { Mode = OrderMode.Careful };
        var composition = OldRun(user, order.Id, RunKind.CrewComposition, RunStatus.Completed, RunOutcome.Converged, "{\"team\":\"old\"}");
        var standard = OldRun(user, order.Id, RunKind.Standard, RunStatus.Completed, RunOutcome.DeliveredWithOpenPoints, OldText,
            parentComposition: composition.Id, at: composition.CreatedAt.AddSeconds(1));
        await host.QueryAsync(async db =>
        {
            db.Orders.Add(order);
            db.Runs.AddRange(composition, standard);
            return await db.SaveChangesAsync();
        });
        return (order.Id, standard.Id);
    }

    private static async Task<Guid> StorePendingOldRunAsync(AtelierEndToEndHost host, string user, RunKind kind, bool withOrder)
    {
        var order = withOrder ? Order(user, "Write a text with an old setup.") with { Mode = OrderMode.Careful, CrewTemplateName = "klassik" } : null;
        var run = OldRun(user, order?.Id, kind, RunStatus.Pending, null, null);
        await host.QueryAsync(async db =>
        {
            if (order is not null)
                db.Orders.Add(order);
            db.Runs.Add(run);
            return await db.SaveChangesAsync();
        });
        return run.Id;
    }

    private static async Task<Guid> StoreRunningOldRunAsync(AtelierEndToEndHost host, string user)
    {
        var order = Order(user, "Write a text that was interrupted.") with { Mode = OrderMode.Careful, CrewTemplateName = "klassik" };
        var run = OldRun(user, order.Id, RunKind.Standard, RunStatus.Running, null, null, at: DateTimeOffset.UtcNow.AddMinutes(-5));
        await host.QueryAsync(async db =>
        {
            db.Orders.Add(order);
            db.Runs.Add(run);
            return await db.SaveChangesAsync();
        });
        return run.Id;
    }

    private static RunEntity OldRun(string user, Guid? orderId, RunKind kind, RunStatus status, RunOutcome? outcome, string? text,
        Guid? parentComposition = null, DateTimeOffset? at = null)
    {
        var created = at ?? DateTimeOffset.UtcNow;
        var finished = status is RunStatus.Completed or RunStatus.Failed;
        return new RunEntity
        {
            Id = Guid.NewGuid(), CreatedAt = created, Status = status, BriefingText = "Write a text with an old setup.", ConfigJson = "{}",
            CreatedByUser = user, Kind = kind, OrderId = orderId, OrderVersion = orderId is null ? null : 1,
            CrewTemplateName = kind == RunKind.Standard ? "klassik" : null, CrewSnapshot = TestCrewSnapshots.RetiredKlassikJson,
            StartedAt = status == RunStatus.Pending ? null : created, CompletedAt = finished ? created.AddMinutes(1) : null,
            FinalText = text, WordCount = text is null ? null : 8, Outcome = outcome, DeliveredIteration = outcome is null ? null : 1,
            ParentCompositionRunId = parentComposition,
        };
    }
}
