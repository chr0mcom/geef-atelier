using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Infrastructure.Persistence.Orders;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Orders;

/// <summary><see cref="OrderRepository.TryAddRevisionRunAsync"/> and the shown run of the list, against real Postgres.</summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class OrderRepositoryRevisionTests(PostgresFixture fixture)
{
    private const string Change = "The meeting is on 5 May, not 12 May.";
    private static readonly DateTimeOffset T0 = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_change_of_the_shown_version_is_stored_as_the_next_version_with_its_hint()
    {
        var (order, v1) = await OrderWithDeliveredVersionAsync();
        var revision = Revision(order, v1);

        (RevisionStart, int?) outcome;
        await using (var db = fixture.NewContext())
            outcome = await new OrderRepository(db).TryAddRevisionRunAsync(order.Id, v1.Id, revision, 10);

        Assert.Equal((RevisionStart.Created, 2), outcome);
        await using var check = fixture.NewContext();
        var stored = await check.Runs.AsNoTracking().SingleAsync(r => r.Id == revision.Id);
        Assert.Equal((order.Id, 2, RunStatus.Pending, RunKind.Standard), (stored.OrderId!.Value, stored.OrderVersion!.Value, stored.Status, stored.Kind));
        Assert.Equal(v1.Id, stored.ParentRunId);
        Assert.Equal("Version one.", stored.SeedDraftText);
        Assert.Equal(Change, stored.RevisionHint);
        Assert.Null((await check.Runs.AsNoTracking().SingleAsync(r => r.Id == v1.Id)).RevisionHint);
        Assert.True((await check.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).UpdatedAt > T0);
    }

    [Theory]
    [InlineData(RunStatus.Pending)]
    [InlineData(RunStatus.Running)]
    public async Task A_pending_or_running_run_makes_the_change_busy(RunStatus status)
    {
        var (order, v1) = await OrderWithDeliveredVersionAsync();
        await AddAsync(Run(order.CreatedByUser, order.Id, 2, status: status, at: T0.AddMinutes(1)));

        await AssertRefusedAsync(order.Id, v1.Id, Revision(order, v1), 10, RevisionStart.Busy);
    }

    [Fact]
    public async Task A_running_composition_makes_the_change_busy_too()
    {
        var (order, v1) = await OrderWithDeliveredVersionAsync();
        await AddAsync(Run(order.CreatedByUser, order.Id, 1, kind: RunKind.CrewComposition, status: RunStatus.Running, at: T0.AddMinutes(1)));

        await AssertRefusedAsync(order.Id, v1.Id, Revision(order, v1), 10, RevisionStart.Busy);
    }

    [Fact]
    public async Task A_base_that_is_no_longer_the_shown_version_is_refused()
    {
        var (order, v1) = await OrderWithDeliveredVersionAsync();
        var v2 = Run(order.CreatedByUser, order.Id, 2, status: RunStatus.Completed, at: T0.AddMinutes(1), finalText: "Version two.");
        await AddAsync(v2);

        await AssertRefusedAsync(order.Id, v1.Id, Revision(order, v1), 10, RevisionStart.BaseNotShown);

        await using var db = fixture.NewContext();
        Assert.Equal((RevisionStart.Created, 3), await new OrderRepository(db).TryAddRevisionRunAsync(order.Id, v2.Id, Revision(order, v2), 10));
    }

    [Fact]
    public async Task After_a_failed_version_the_shown_one_stays_the_base_and_the_number_goes_on()
    {
        var (order, v1) = await OrderWithDeliveredVersionAsync();
        await AddAsync(Run(order.CreatedByUser, order.Id, 2, status: RunStatus.Failed, at: T0.AddMinutes(1)));

        await AssertRefusedAsync(order.Id, v1.Id, Revision(order, v1, version: 2), 10, RevisionStart.BaseNotShown);

        await using var db = fixture.NewContext();
        Assert.Equal((RevisionStart.Created, 3), await new OrderRepository(db).TryAddRevisionRunAsync(order.Id, v1.Id, Revision(order, v1, version: 3), 10));
    }

    [Fact]
    public async Task The_most_versions_stop_the_change()
    {
        var (order, v1) = await OrderWithDeliveredVersionAsync();
        await AddAsync(Run(order.CreatedByUser, order.Id, 2, status: RunStatus.Failed, at: T0.AddMinutes(1)));

        await AssertRefusedAsync(order.Id, v1.Id, Revision(order, v1), 2, RevisionStart.TooManyVersions);
    }

    [Fact]
    public async Task A_stopped_order_and_an_unknown_order_are_refused()
    {
        var (order, v1) = await OrderWithDeliveredVersionAsync();
        await using (var db = fixture.NewContext())
            await db.Orders.Where(o => o.Id == order.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.State, OrderState.Cancelled));

        await AssertRefusedAsync(order.Id, v1.Id, Revision(order, v1), 10, RevisionStart.Cancelled);
        var unknown = Guid.NewGuid();
        await AssertRefusedAsync(unknown, v1.Id, Revision(order, v1), 10, RevisionStart.NotFound, checkOrder: order.Id);
    }

    [Fact]
    public async Task Ten_calls_at_the_same_time_create_exactly_one_version()
    {
        var (order, v1) = await OrderWithDeliveredVersionAsync();
        using var start = new ManualResetEventSlim(false);

        var calls = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            start.Wait();
            await using var db = fixture.NewContext();
            return await new OrderRepository(db).TryAddRevisionRunAsync(order.Id, v1.Id, Revision(order, v1), 10);
        })).ToList();
        start.Set();
        var outcomes = await Task.WhenAll(calls);

        Assert.Equal((RevisionStart.Created, 2), Assert.Single(outcomes, o => o.Result == RevisionStart.Created));
        Assert.All(outcomes.Where(o => o.Result != RevisionStart.Created), o => Assert.Equal((RevisionStart.Busy, (int?)null), o));
        await using var check = fixture.NewContext();
        Assert.Equal([1, 2], await check.Runs.Where(r => r.OrderId == order.Id).Select(r => r.OrderVersion!.Value).OrderBy(v => v).ToListAsync());
    }

    [Fact]
    public async Task The_list_carries_the_shown_run_beside_the_current_one()
    {
        var (order, v1) = await OrderWithDeliveredVersionAsync();
        await AddAsync(Run(order.CreatedByUser, order.Id, 2, status: RunStatus.Running, at: T0.AddMinutes(1)));

        await using var db = fixture.NewContext();
        var row = Assert.Single(await new OrderRepository(db).ListAsync(order.CreatedByUser, 10));

        Assert.Equal(2, row.CurrentRun!.OrderVersion);
        Assert.Equal(v1.Id, row.ShownRun!.Id);
        Assert.Equal(OrderRepository.TextMarker, row.ShownRun.FinalText);
        Assert.Equal(2, row.ShownRun.WordCount);
    }

    private async Task<(OrderEntity Order, RunEntity V1)> OrderWithDeliveredVersionAsync()
    {
        var order = Order(NewUser(), at: T0);
        var v1 = Run(order.CreatedByUser, order.Id, 1, status: RunStatus.Completed, at: T0, finalText: "Version one.");
        v1.WordCount = 2;
        await using var db = fixture.NewContext();
        await new OrderRepository(db).CreateWithFirstRunAsync(order, [], v1);
        return (order, v1);
    }

    private async Task AddAsync(RunEntity run)
    {
        await using var db = fixture.NewContext();
        db.Runs.Add(run);
        await db.SaveChangesAsync();
    }

    private async Task AssertRefusedAsync(Guid orderId, Guid baseRunId, RunEntity revision, int maxVersions, RevisionStart expected, Guid? checkOrder = null)
    {
        await using (var db = fixture.NewContext())
        {
            Assert.Equal((expected, (int?)null), await new OrderRepository(db).TryAddRevisionRunAsync(orderId, baseRunId, revision, maxVersions));
            Assert.Empty(db.ChangeTracker.Entries());
        }

        await using var check = fixture.NewContext();
        Assert.False(await check.Runs.AnyAsync(r => r.Id == revision.Id));
        Assert.False(await check.Runs.AnyAsync(r => r.OrderId == (checkOrder ?? orderId) && r.RevisionHint != null));
    }

    private static RunEntity Revision(OrderEntity order, RunEntity baseRun, int? version = null) => new()
    {
        Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow, Status = RunStatus.Pending, BriefingText = order.RequestText, ConfigJson = "{}",
        CreatedByUser = order.CreatedByUser, Kind = RunKind.Standard, OrderVersion = version, ParentRunId = baseRun.Id,
        SeedDraftText = baseRun.FinalText, RevisionHint = Change,
    };
}
