using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Infrastructure.Persistence.Orders;
using Geef.Atelier.Infrastructure.Persistence.Sources;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Orders;

/// <summary>The intake methods of <see cref="OrderRepository"/> against real Postgres: claim, stuck and unanswered lists, questions and the atomic start.</summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class OrderRepositoryIntakeTests(PostgresFixture fixture)
{
    private const string ClarifyJson = """{"schemaVersion":1,"status":"Ok","questions":[]}""";

    [Fact]
    public async Task Two_simultaneous_claims_have_exactly_one_winner()
    {
        var now = DateTimeOffset.UtcNow;
        var order = await AddAsync(Order(NewUser(), state: OrderState.Analyzing, at: now));

        for (var round = 0; round < 5; round++)
        {
            var target = round == 0 ? order : await AddAsync(Order(NewUser(), state: OrderState.Analyzing, at: now));
            await using var db1 = fixture.NewContext();
            await using var db2 = fixture.NewContext();
            var results = await Task.WhenAll(
                new OrderRepository(db1).TryClaimForIntakeAsync(target.Id, now, now.AddSeconds(-90), 2),
                new OrderRepository(db2).TryClaimForIntakeAsync(target.Id, now, now.AddSeconds(-90), 2));

            Assert.Equal(1, results.Count(r => r));
            var stored = await LoadAsync(target.Id);
            Assert.Equal(1, stored.IntakeAttempts);
            Assert.Equal(now, stored.ClaimedAt!.Value, TimeSpan.FromMilliseconds(1));
            Assert.Equal(now, stored.IntakeStartedAt!.Value, TimeSpan.FromMilliseconds(1));
        }
    }

    [Fact]
    public async Task A_stale_claim_is_free_again_until_the_attempts_are_used_up()
    {
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-10);
        var order = await AddAsync(Order(NewUser(), state: OrderState.Analyzing, at: t0));
        await using var db = fixture.NewContext();
        var repository = new OrderRepository(db);

        Assert.True(await repository.TryClaimForIntakeAsync(order.Id, t0, t0.AddSeconds(-90), 2));
        Assert.False(await repository.TryClaimForIntakeAsync(order.Id, t0.AddSeconds(30), t0.AddSeconds(-60), 2));
        Assert.DoesNotContain(order.Id, await repository.ListIntakeCandidatesAsync(t0.AddSeconds(-60), 2, 1_000));

        var later = t0.AddSeconds(120);
        Assert.Contains(order.Id, await repository.ListIntakeCandidatesAsync(later.AddSeconds(-90), 2, 1_000));
        Assert.True(await repository.TryClaimForIntakeAsync(order.Id, later, later.AddSeconds(-90), 2));
        Assert.Equal(2, (await LoadAsync(order.Id)).IntakeAttempts);

        var muchLater = later.AddMinutes(5);
        Assert.DoesNotContain(order.Id, await repository.ListIntakeCandidatesAsync(muchLater.AddSeconds(-90), 2, 1_000));
        Assert.False(await repository.TryClaimForIntakeAsync(order.Id, muchLater, muchLater.AddSeconds(-90), 2));
        Assert.Contains(order.Id, await repository.ListStuckIntakesAsync(muchLater.AddSeconds(-90), muchLater.AddSeconds(-60), 2, 1_000));
    }

    [Fact]
    public async Task Candidates_are_only_analyzing_orders_oldest_first()
    {
        var t0 = DateTimeOffset.UtcNow.AddDays(-3);
        var newer = await AddAsync(Order(NewUser(), state: OrderState.Analyzing, at: t0.AddSeconds(2)));
        var older = await AddAsync(Order(NewUser(), state: OrderState.Analyzing, at: t0.AddSeconds(1)));
        var ready = await AddAsync(Order(NewUser(), state: OrderState.Ready, at: t0));
        var waiting = await AddAsync(Order(NewUser(), state: OrderState.NeedsInput, at: t0));

        await using var db = fixture.NewContext();
        var candidates = await new OrderRepository(db).ListIntakeCandidatesAsync(DateTimeOffset.UtcNow, 2, 10_000);

        Assert.True(candidates.ToList().IndexOf(older.Id) < candidates.ToList().IndexOf(newer.Id));
        Assert.Contains(newer.Id, candidates);
        Assert.DoesNotContain(ready.Id, candidates);
        Assert.DoesNotContain(waiting.Id, candidates);
        Assert.Empty(await new OrderRepository(db).ListIntakeCandidatesAsync(DateTimeOffset.UtcNow, 2, 0));
    }

    [Fact]
    public async Task Stuck_lists_unclaimed_orders_after_the_queue_timeout_but_not_fresh_ones()
    {
        var now = DateTimeOffset.UtcNow;
        var waitingLong = await AddAsync(Order(NewUser(), state: OrderState.Analyzing, at: now.AddSeconds(-120)));
        var fresh = await AddAsync(Order(NewUser(), state: OrderState.Analyzing, at: now.AddSeconds(-10)));
        var claimed = await AddAsync(Order(NewUser(), state: OrderState.Analyzing, at: now.AddSeconds(-120)));
        await using var db = fixture.NewContext();
        var repository = new OrderRepository(db);
        Assert.True(await repository.TryClaimForIntakeAsync(claimed.Id, now.AddSeconds(-20), now.AddSeconds(-110), 2));

        var stuck = await repository.ListStuckIntakesAsync(now.AddSeconds(-90), now.AddSeconds(-60), 2, 10_000);

        Assert.Contains(waitingLong.Id, stuck);
        Assert.DoesNotContain(fresh.Id, stuck);
        Assert.DoesNotContain(claimed.Id, stuck);
    }

    [Fact]
    public async Task Questions_are_saved_only_from_analyzing()
    {
        var at = DateTimeOffset.UtcNow;
        var analyzing = await AddAsync(Order(NewUser(), state: OrderState.Analyzing));
        var ready = await AddAsync(Order(NewUser(), state: OrderState.Ready));
        await using var db = fixture.NewContext();
        var repository = new OrderRepository(db);

        Assert.True(await repository.TrySaveQuestionsAsync(analyzing.Id, ClarifyJson, at));
        Assert.False(await repository.TrySaveQuestionsAsync(analyzing.Id, ClarifyJson, at));
        Assert.False(await repository.TrySaveQuestionsAsync(ready.Id, ClarifyJson, at));

        var stored = await LoadAsync(analyzing.Id);
        Assert.Equal(OrderState.NeedsInput, stored.State);
        Assert.Equal(IntakeOutcome.Asked, stored.IntakeOutcome);
        Assert.Equal(at, stored.QuestionsAskedAt!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Equal(at, stored.IntakeFinishedAt!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Contains("\"schemaVersion\"", stored.ClarifyJson, StringComparison.Ordinal);
        Assert.Null((await LoadAsync(ready.Id)).ClarifyJson);
    }

    [Fact]
    public async Task Unanswered_lists_waiting_orders_asked_before_the_limit()
    {
        var now = DateTimeOffset.UtcNow;
        var old = await AddAsync(Order(NewUser(), state: OrderState.Analyzing));
        var recent = await AddAsync(Order(NewUser(), state: OrderState.Analyzing));
        await using var db = fixture.NewContext();
        var repository = new OrderRepository(db);
        await repository.TrySaveQuestionsAsync(old.Id, ClarifyJson, now.AddHours(-25));
        await repository.TrySaveQuestionsAsync(recent.Id, ClarifyJson, now.AddHours(-1));

        var unanswered = await repository.ListUnansweredAsync(now.AddHours(-24), 10_000);

        Assert.Contains(old.Id, unanswered);
        Assert.DoesNotContain(recent.Id, unanswered);
    }

    [Fact]
    public async Task Start_from_analyzing_sets_everything_in_one_transaction()
    {
        var user = NewUser();
        var at = DateTimeOffset.UtcNow;
        var order = await AddAsync(Order(user, state: OrderState.Analyzing));
        var run = Run(user, order.Id, 1);
        await using var db = fixture.NewContext();

        var started = await new OrderRepository(db).TryStartWithRunAsync(new OrderStart(
            order.Id, OrderState.Analyzing, run, ClarifyJson, null, IntakeOutcome.Clarified, [], at));

        Assert.True(started);
        var stored = await LoadAsync(order.Id);
        Assert.Equal(OrderState.Ready, stored.State);
        Assert.Equal(IntakeOutcome.Clarified, stored.IntakeOutcome);
        Assert.Equal(at, stored.IntakeFinishedAt!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Null(stored.AnsweredAt);
        Assert.NotNull(stored.ClarifyJson);
        Assert.Null(stored.AnswersJson);
        await using var check = fixture.NewContext();
        var storedRun = await check.Runs.AsNoTracking().SingleAsync(r => r.OrderId == order.Id);
        Assert.Equal((run.Id, 1), (storedRun.Id, storedRun.OrderVersion!.Value));
    }

    [Fact]
    public async Task Start_from_needs_input_keeps_the_record_and_sets_answers_and_roles()
    {
        var user = NewUser();
        var at = DateTimeOffset.UtcNow;
        var template = Source(user);
        var other = Source(user);
        await using (var db = fixture.NewContext())
        {
            var sources = new SourceRepository(db);
            await sources.AddAsync(template);
            await sources.AddAsync(other);
        }

        var order = Order(user, state: OrderState.Analyzing);
        await using (var db = fixture.NewContext())
            await new OrderRepository(db).CreateAsync(order, [Link(order.Id, template.Id, 0), Link(order.Id, other.Id, 1)]);
        await using (var db = fixture.NewContext())
            Assert.True(await new OrderRepository(db).TrySaveQuestionsAsync(order.Id, ClarifyJson, at.AddMinutes(-3)));

        await using (var db = fixture.NewContext())
        {
            var started = await new OrderRepository(db).TryStartWithRunAsync(new OrderStart(
                order.Id, OrderState.NeedsInput, Run(user, order.Id, 1), null, """[{"questionId":"q1","decideForMe":true}]""",
                IntakeOutcome.Answered, [(template.Id, SourceRole.TemplateToRevise)], at));
            Assert.True(started);
        }

        var stored = await LoadAsync(order.Id);
        Assert.Equal(OrderState.Ready, stored.State);
        Assert.Equal(IntakeOutcome.Answered, stored.IntakeOutcome);
        Assert.Equal(at, stored.AnsweredAt!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Equal(at.AddMinutes(-3), stored.IntakeFinishedAt!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Contains("\"schemaVersion\"", stored.ClarifyJson, StringComparison.Ordinal);
        Assert.Contains("\"questionId\"", stored.AnswersJson, StringComparison.Ordinal);
        await using var check = fixture.NewContext();
        var roles = await check.OrderSources.AsNoTracking().Where(l => l.OrderId == order.Id).ToDictionaryAsync(l => l.SourceId, l => l.Role);
        Assert.Equal(SourceRole.TemplateToRevise, roles[template.Id]);
        Assert.Equal(SourceRole.Facts, roles[other.Id]);
    }

    [Fact]
    public async Task Start_in_the_wrong_state_or_twice_returns_false_without_a_run()
    {
        var user = NewUser();
        var order = await AddAsync(Order(user, state: OrderState.Analyzing));
        await using var db = fixture.NewContext();
        var repository = new OrderRepository(db);

        Assert.False(await repository.TryStartWithRunAsync(new OrderStart(
            order.Id, OrderState.NeedsInput, Run(user, order.Id, 1), null, null, IntakeOutcome.Answered, [], DateTimeOffset.UtcNow)));
        var missing = Guid.NewGuid();
        Assert.False(await repository.TryStartWithRunAsync(new OrderStart(
            missing, OrderState.Analyzing, Run(user, missing, 1), null, null, IntakeOutcome.Skipped, [], DateTimeOffset.UtcNow)));
        Assert.Equal(0, await CountRunsAsync(order.Id));
        Assert.Equal(OrderState.Analyzing, (await LoadAsync(order.Id)).State);

        Assert.True(await repository.TryStartWithRunAsync(new OrderStart(
            order.Id, OrderState.Analyzing, Run(user, order.Id, 1), null, null, IntakeOutcome.Skipped, [], DateTimeOffset.UtcNow)));
        Assert.False(await repository.TryStartWithRunAsync(new OrderStart(
            order.Id, OrderState.Analyzing, Run(user, order.Id, 1), null, null, IntakeOutcome.Skipped, [], DateTimeOffset.UtcNow)));
        Assert.False(await repository.TryStartWithRunAsync(new OrderStart(
            order.Id, OrderState.Ready, Run(user, order.Id, 1), null, null, IntakeOutcome.Skipped, [], DateTimeOffset.UtcNow)));
        Assert.Equal(1, await CountRunsAsync(order.Id));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Two_simultaneous_starts_create_exactly_one_run()
    {
        var user = NewUser();
        var order = await AddAsync(Order(user, state: OrderState.NeedsInput));
        await using var db1 = fixture.NewContext();
        await using var db2 = fixture.NewContext();

        var results = await Task.WhenAll(
            new OrderRepository(db1).TryStartWithRunAsync(new OrderStart(
                order.Id, OrderState.NeedsInput, Run(user, order.Id, 1), null, null, IntakeOutcome.Answered, [], DateTimeOffset.UtcNow)),
            new OrderRepository(db2).TryStartWithRunAsync(new OrderStart(
                order.Id, OrderState.NeedsInput, Run(user, order.Id, 1), null, null, IntakeOutcome.Skipped, [], DateTimeOffset.UtcNow)));

        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(1, await CountRunsAsync(order.Id));
    }

    [Fact]
    public async Task A_run_that_does_not_fit_the_order_is_rejected()
    {
        var user = NewUser();
        var order = await AddAsync(Order(user, state: OrderState.Analyzing));
        await using var db = fixture.NewContext();
        var repository = new OrderRepository(db);

        await Assert.ThrowsAsync<ArgumentException>(() => repository.TryStartWithRunAsync(new OrderStart(
            order.Id, OrderState.Analyzing, Run(user, Guid.NewGuid(), 1), null, null, null, [], DateTimeOffset.UtcNow)));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.TryStartWithRunAsync(new OrderStart(
            order.Id, OrderState.Analyzing, Run(user, order.Id, 2), null, null, null, [], DateTimeOffset.UtcNow)));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.TryStartWithRunAsync(new OrderStart(
            order.Id, OrderState.Analyzing, Run(user, order.Id, 1, RunKind.CrewComposition), null, null, null, [], DateTimeOffset.UtcNow)));
    }

    private async Task<OrderEntity> AddAsync(OrderEntity order)
    {
        await using var db = fixture.NewContext();
        await new OrderRepository(db).CreateAsync(order, []);
        return order;
    }

    private async Task<OrderEntity> LoadAsync(Guid orderId)
    {
        await using var db = fixture.NewContext();
        return await db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
    }

    private async Task<int> CountRunsAsync(Guid orderId)
    {
        await using var db = fixture.NewContext();
        return await db.Runs.CountAsync(r => r.OrderId == orderId);
    }
}
