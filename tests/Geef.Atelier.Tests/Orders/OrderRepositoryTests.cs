using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Infrastructure.Persistence.Orders;
using Geef.Atelier.Infrastructure.Persistence.Sources;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Orders;

/// <summary><see cref="OrderRepository"/> against real Postgres.</summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class OrderRepositoryTests(PostgresFixture fixture)
{
    [Fact]
    public async Task CreateWithFirstRunStoresOrderLinksAndRunAndClearsExpiry()
    {
        var user = NewUser();
        var source = Source(user, expiresAt: DateTimeOffset.UtcNow.AddHours(1));
        await using (var db = fixture.NewContext())
            await new SourceRepository(db).AddAsync(source);

        var order = Order(user);
        var run = Run(user, order.Id, 1);
        await using (var db = fixture.NewContext())
            await new OrderRepository(db).CreateWithFirstRunAsync(order, [Link(order.Id, source.Id)], run);

        await using var check = fixture.NewContext();
        Assert.Equal(order.RequestText, (await check.Orders.SingleAsync(o => o.Id == order.Id)).RequestText);
        var stored = await check.Runs.SingleAsync(r => r.Id == run.Id);
        Assert.Equal(order.Id, stored.OrderId);
        Assert.Equal(1, stored.OrderVersion);
        Assert.Null((await check.Sources.SingleAsync(s => s.Id == source.Id)).ExpiresAt);
    }

    [Fact]
    public async Task CreateWithFirstRunReportsADeletedSourceAndLeavesTheContextClean()
    {
        var user = NewUser();
        var order = Order(user);
        await using var db = fixture.NewContext();
        var repository = new OrderRepository(db);

        await Assert.ThrowsAsync<OrderSourceMissingException>(() =>
            repository.CreateWithFirstRunAsync(order, [Link(order.Id, Guid.NewGuid())], Run(user, order.Id, 1)));
        Assert.Empty(db.ChangeTracker.Entries());

        var retry = Order(user);
        await repository.CreateWithFirstRunAsync(retry, [], Run(user, retry.Id, 1));

        await using var check = fixture.NewContext();
        Assert.False(await check.Orders.AnyAsync(o => o.Id == order.Id));
        Assert.False(await check.Runs.AnyAsync(r => r.OrderId == order.Id));
        Assert.Equal(1, await check.Runs.CountAsync(r => r.OrderId == retry.Id));
    }

    [Fact]
    public async Task CreateWithFirstRunIsAtomic()
    {
        var user = NewUser();
        var existing = Run(user);
        await using (var db = fixture.NewContext())
        {
            db.Runs.Add(existing);
            await db.SaveChangesAsync();
        }

        var order = Order(user);
        var clash = Run(user, order.Id, 1);
        var duplicateRun = new RunEntity
        {
            Id = existing.Id, CreatedAt = clash.CreatedAt, Status = RunStatus.Pending, BriefingText = "b", ConfigJson = "{}",
            OrderId = order.Id, OrderVersion = 1,
        };

        await using (var db = fixture.NewContext())
            await Assert.ThrowsAsync<DbUpdateException>(() => new OrderRepository(db).CreateWithFirstRunAsync(order, [], duplicateRun));

        await using var check = fixture.NewContext();
        Assert.False(await check.Orders.AnyAsync(o => o.Id == order.Id));
    }

    [Fact]
    public async Task AVersionCannotStartTwiceButAddRunTakesTheNextVersion()
    {
        var user = NewUser();
        var order = Order(user);
        await using (var db = fixture.NewContext())
            await new OrderRepository(db).CreateWithFirstRunAsync(order, [], Run(user, order.Id, 1));

        await using (var db = fixture.NewContext())
        {
            db.Runs.Add(Run(user, order.Id, 1));
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(ex.InnerException).SqlState);
        }

        await using (var db = fixture.NewContext())
        {
            var repo = new OrderRepository(db);
            Assert.Equal(2, await repo.AddRunAsync(order.Id, Run(user)));
            Assert.Equal(3, await repo.AddRunAsync(order.Id, Run(user)));
            Assert.Equal(2, await repo.AddRunAsync(order.Id, Run(user, kind: RunKind.CrewComposition, version: 2)));
            await Assert.ThrowsAsync<InvalidOperationException>(() => repo.AddRunAsync(Guid.NewGuid(), Run(user)));
        }

        await using var check = fixture.NewContext();
        var versions = await check.Runs.Where(r => r.OrderId == order.Id && r.Kind == RunKind.Standard).Select(r => r.OrderVersion).OrderBy(v => v).ToListAsync();
        Assert.Equal([1, 2, 3], versions);
    }

    [Fact]
    public async Task ListAsyncReturnsOnlyTheOwnersOrdersNewestActivityFirstWithCurrentRunAndSourceCount()
    {
        var user = NewUser();
        var other = NewUser();
        var t = DateTimeOffset.UtcNow;
        var older = Order(user, "older", at: t.AddMinutes(-10));
        var newer = Order(user, "newer", at: t.AddMinutes(-5));
        var foreign = Order(other, "foreign", at: t);
        var source = Source(user);
        await using (var db = fixture.NewContext())
        {
            await new SourceRepository(db).AddAsync(source);
            var repo = new OrderRepository(db);
            await repo.CreateWithFirstRunAsync(older, [Link(older.Id, source.Id)], Run(user, older.Id, 1, status: RunStatus.Completed, finalText: "v1"));
            await repo.CreateWithFirstRunAsync(newer, [], Run(user, newer.Id, 1));
            await repo.CreateWithFirstRunAsync(foreign, [], Run(other, foreign.Id, 1));
            await repo.AddRunAsync(older.Id, Run(user, status: RunStatus.Running));
        }

        await using var read = fixture.NewContext();
        var rows = await new OrderRepository(read).ListAsync(user, 10);

        Assert.Equal(["older", "newer"], rows.Select(r => r.Order.RequestText));
        Assert.Equal(2, rows[0].CurrentRun!.OrderVersion);
        Assert.Equal(1, rows[0].SourceCount);
        Assert.Equal(0, rows[1].SourceCount);
        Assert.DoesNotContain(rows, r => r.Order.CreatedByUser == other);
        Assert.Contains(await new OrderRepository(read).ListAsync(null, 500), r => r.Order.Id == foreign.Id);
    }

    [Fact]
    public async Task ListAsyncLoadsOnlyWhatTheListNeedsOfTheCurrentRun()
    {
        var user = NewUser();
        var withText = Order(user, "with text");
        var blank = Order(user, "blank text");
        var run = Run(user, withText.Id, 1, status: RunStatus.Completed, finalText: "A long delivered text", briefing: "Secret briefing");
        run.WordCount = 4;
        await using (var db = fixture.NewContext())
        {
            var repo = new OrderRepository(db);
            await repo.CreateWithFirstRunAsync(withText, [], run);
            await repo.CreateWithFirstRunAsync(blank, [], Run(user, blank.Id, 1, status: RunStatus.Failed, finalText: "  \n "));
        }

        await using var read = fixture.NewContext();
        var rows = await new OrderRepository(read).ListAsync(user, 10);

        var current = rows.Single(r => r.Order.Id == withText.Id).CurrentRun!;
        Assert.Equal(run.Id, current.Id);
        Assert.Equal(RunStatus.Completed, current.Status);
        Assert.Equal(4, current.WordCount);
        Assert.True(current.ToRunState(DateTimeOffset.UtcNow).HasText);
        Assert.Equal(string.Empty, current.BriefingText);
        Assert.Null(current.CrewSnapshot);
        Assert.NotEqual("A long delivered text", current.FinalText);
        Assert.False(rows.Single(r => r.Order.Id == blank.Id).CurrentRun!.ToRunState(DateTimeOffset.UtcNow).HasText);
    }

    [Fact]
    public async Task AFailedCreateLeavesNothingBehindForTheNextTry()
    {
        var user = NewUser();
        await using var db = fixture.NewContext();
        var repo = new OrderRepository(db);
        var first = Order(user, "first try");
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            repo.CreateWithFirstRunAsync(first, [Link(first.Id, Guid.NewGuid())], Run(user, first.Id, 1)));

        var second = Order(user, "second try");
        await repo.CreateWithFirstRunAsync(second, [], Run(user, second.Id, 1));

        await using var check = fixture.NewContext();
        Assert.Equal(["second try"], await check.Orders.Where(o => o.CreatedByUser == user).Select(o => o.RequestText).ToListAsync());
        Assert.Equal(1, await check.Runs.CountAsync(r => r.CreatedByUser == user));
    }

    [Fact]
    public async Task TrySetStateOnlyMovesFromTheExpectedState()
    {
        var order = Order(NewUser(), state: OrderState.Analyzing);
        await using var db = fixture.NewContext();
        var repo = new OrderRepository(db);
        await repo.CreateAsync(order, []);

        Assert.False(await repo.TrySetStateAsync(order.Id, OrderState.Ready, OrderState.Cancelled));
        Assert.True(await repo.TrySetStateAsync(order.Id, OrderState.Analyzing, OrderState.Cancelled));
        Assert.Equal(OrderState.Cancelled, (await repo.GetAsync(order.Id))!.State);
    }

    [Fact]
    public async Task LinkLegacyRunCreatesInheritsAndVersions()
    {
        var user = NewUser();
        var composition = Run(user, kind: RunKind.CrewComposition, status: RunStatus.Completed,
            briefing: "Compose a crew for the following task:\n\nPlan a launch.", config: """{"original_task":"Plan a launch."}""");
        await using (var db = fixture.NewContext())
        {
            db.Runs.Add(composition);
            await db.SaveChangesAsync();
        }

        await using var ctx = fixture.NewContext();
        var repo = new OrderRepository(ctx);
        var orderId = await repo.LinkLegacyRunAsync(composition.Id, OrderEntry.Mcp);
        Assert.Equal(composition.Id, orderId);
        var order = (await repo.GetAsync(orderId))!;
        Assert.Equal("Plan a launch.", order.RequestText);
        Assert.Equal(OrderMode.Careful, order.Mode);
        Assert.Equal(OrderEntry.Mcp, order.Entry);
        Assert.Equal((orderId, (int?)1), await repo.GetLinkForRunAsync(composition.Id));

        var task = Run(user, status: RunStatus.Failed, parentCompositionRunId: composition.Id);
        var resume = Run(user, parentRunId: task.Id);
        var learning = Run(user, kind: RunKind.Learning);
        ctx.Runs.AddRange(task, resume, learning);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        Assert.Equal(orderId, await repo.LinkLegacyRunAsync(task.Id, OrderEntry.Web));
        Assert.Equal(orderId, await repo.LinkLegacyRunAsync(resume.Id, OrderEntry.Web));
        Assert.Equal((orderId, (int?)1), await repo.GetLinkForRunAsync(task.Id));
        Assert.Equal((orderId, (int?)2), await repo.GetLinkForRunAsync(resume.Id));
        Assert.Equal(orderId, await repo.LinkLegacyRunAsync(resume.Id, OrderEntry.Web));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.LinkLegacyRunAsync(learning.Id, OrderEntry.Web));
        Assert.Null(await repo.GetLinkForRunAsync(learning.Id));
        Assert.Equal(1, await ctx.Orders.CountAsync(o => o.CreatedByUser == user));
    }

    [Fact]
    public async Task ListRunEventsReadsTheIterationFromThePayload()
    {
        var order = Order(NewUser());
        var run = Run(order.CreatedByUser, order.Id, 1);
        var t = DateTimeOffset.UtcNow;
        await using var db = fixture.NewContext();
        await new OrderRepository(db).CreateWithFirstRunAsync(order, [], run);
        db.Events.Add(new EventEntity { Id = 0, RunId = run.Id, EventType = "ExecutionStartedEvent", PayloadJson = """{"Iteration": 2}""", CreatedAt = t.AddSeconds(2) });
        db.Events.Add(new EventEntity { Id = 0, RunId = run.Id, EventType = "PipelineStartedEvent", PayloadJson = "{}", CreatedAt = t.AddSeconds(1) });
        await db.SaveChangesAsync();

        var events = await new OrderRepository(db).ListRunEventsAsync(run.Id);

        Assert.Equal(["PipelineStartedEvent", "ExecutionStartedEvent"], events.Select(e => e.EventType));
        Assert.Null(events[0].Iteration);
        Assert.Equal(2, events[1].Iteration);
    }

    [Fact]
    public async Task DeleteCascadesToRunsAndLinksAndLetsOrphanedSourcesExpire()
    {
        var user = NewUser();
        var shared = Source(user);
        var only = Source(user);
        var library = Source(user) with { InLibrary = true };
        var first = Order(user);
        var second = Order(user);
        var run = Run(user, first.Id, 1, status: RunStatus.Completed);
        await using (var db = fixture.NewContext())
        {
            var sources = new SourceRepository(db);
            await sources.AddAsync(shared);
            await sources.AddAsync(only);
            await sources.AddAsync(library);
            var repo = new OrderRepository(db);
            await repo.CreateWithFirstRunAsync(first, [Link(first.Id, shared.Id), Link(first.Id, only.Id, 1), Link(first.Id, library.Id, 2)], run);
            await repo.CreateWithFirstRunAsync(second, [Link(second.Id, shared.Id)], Run(user, second.Id, 1));
        }

        await using var ctx = fixture.NewContext();
        Assert.True(await new OrderRepository(ctx).DeleteAsync(first.Id));
        Assert.False(await new OrderRepository(ctx).DeleteAsync(first.Id));

        Assert.False(await ctx.Runs.AnyAsync(r => r.Id == run.Id));
        Assert.False(await ctx.OrderSources.AnyAsync(l => l.OrderId == first.Id));
        Assert.NotNull((await ctx.Sources.SingleAsync(s => s.Id == only.Id)).ExpiresAt);
        Assert.Null((await ctx.Sources.SingleAsync(s => s.Id == shared.Id)).ExpiresAt);
        Assert.Null((await ctx.Sources.SingleAsync(s => s.Id == library.Id)).ExpiresAt);
    }
}
