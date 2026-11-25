using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Orders;
using System.Data.Common;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Persistence.Orders;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
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
        await AssertTenCallsCreateOneVersionAsync(fixture.NewContext);
    }

    [Fact]
    public async Task The_lock_alone_keeps_two_calls_that_would_meet_after_the_checks_to_one_version()
    {
        var connectionString = await DatabaseWithoutVersionIndexAsync();
        try
        {
            await using (var db = NewContext(connectionString))
                Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM pg_indexes WHERE indexname = {VersionIndex}").SingleAsync());
            var (order, v1) = await OrderWithDeliveredVersionAsync(() => NewContext(connectionString));
            var meeting = new MeetingAfterTheChecks(order.Id, 2);

            var outcomes = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
            {
                await using var db = NewContext(connectionString, meeting.ForOneCall());
                return await new OrderRepository(db).TryAddRevisionRunAsync(order.Id, v1.Id, Revision(order, v1), 10);
            })));

            Assert.Equal(2, meeting.Reached);
            Assert.Equal([(RevisionStart.Busy, (int?)null), (RevisionStart.Created, 2)], outcomes.OrderBy(o => o.Result == RevisionStart.Created));
            await using var check = NewContext(connectionString);
            Assert.Equal([1, 2], await check.Runs.Where(r => r.OrderId == order.Id).Select(r => r.OrderVersion!.Value).OrderBy(v => v).ToListAsync());
        }
        finally
        {
            await DropDatabaseAsync(connectionString);
        }
    }

    [Fact]
    public async Task Ten_calls_at_the_same_time_create_exactly_one_version_also_without_the_index()
    {
        var connectionString = await DatabaseWithoutVersionIndexAsync();
        try
        {
            await AssertTenCallsCreateOneVersionAsync(() => NewContext(connectionString));
        }
        finally
        {
            await DropDatabaseAsync(connectionString);
        }
    }

    [Fact]
    public async Task The_index_alone_turns_a_version_written_after_the_checks_into_busy_and_stores_nothing()
    {
        var (order, v1) = await OrderWithDeliveredVersionAsync();
        var revision = Revision(order, v1);
        var race = new VersionWrittenAfterTheChecks(v1.Id);

        await using (var db = NewContext(fixture.ConnectionString, race))
        {
            Assert.Equal((RevisionStart.Busy, (int?)null), await new OrderRepository(db).TryAddRevisionRunAsync(order.Id, v1.Id, revision, 10));
            Assert.Empty(db.ChangeTracker.Entries());
        }

        Assert.True(race.Fired);
        await using var check = fixture.NewContext();
        Assert.False(await check.Runs.AnyAsync(r => r.Id == revision.Id));
        Assert.Equal([1], await check.Runs.Where(r => r.OrderId == order.Id).Select(r => r.OrderVersion!.Value).ToListAsync());
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

    private Task<(OrderEntity Order, RunEntity V1)> OrderWithDeliveredVersionAsync() => OrderWithDeliveredVersionAsync(fixture.NewContext);

    private static async Task<(OrderEntity Order, RunEntity V1)> OrderWithDeliveredVersionAsync(Func<AtelierDbContext> context)
    {
        var order = Order(NewUser(), at: T0);
        var v1 = Run(order.CreatedByUser, order.Id, 1, status: RunStatus.Completed, at: T0, finalText: "Version one.");
        v1.WordCount = 2;
        await using var db = context();
        await new OrderRepository(db).CreateWithFirstRunAsync(order, [], v1);
        return (order, v1);
    }

    private static async Task AssertTenCallsCreateOneVersionAsync(Func<AtelierDbContext> context)
    {
        var (order, v1) = await OrderWithDeliveredVersionAsync(context);
        using var start = new ManualResetEventSlim(false);

        var calls = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            start.Wait();
            await using var db = context();
            return await new OrderRepository(db).TryAddRevisionRunAsync(order.Id, v1.Id, Revision(order, v1), 10);
        })).ToList();
        start.Set();
        var outcomes = await Task.WhenAll(calls);

        Assert.Equal((RevisionStart.Created, 2), Assert.Single(outcomes, o => o.Result == RevisionStart.Created));
        Assert.All(outcomes.Where(o => o.Result != RevisionStart.Created), o => Assert.Equal((RevisionStart.Busy, (int?)null), o));
        await using var check = context();
        Assert.Equal([1, 2], await check.Runs.Where(r => r.OrderId == order.Id).Select(r => r.OrderVersion!.Value).OrderBy(v => v).ToListAsync());
    }

    private const string VersionIndex = "UX_Runs_OrderId_OrderVersion_Standard";

    private static AtelierDbContext NewContext(string connectionString, params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<AtelierDbContext>()
            .UseNpgsql(connectionString)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .AddInterceptors(interceptors)
            .Options);

    private async Task<string> DatabaseWithoutVersionIndexAsync()
    {
        var name = $"revise_lock_{Guid.NewGuid():N}";
        await using (var server = new NpgsqlConnection(fixture.ConnectionString))
        {
            await server.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", server);
            await create.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = name }.ConnectionString;
        await using var db = NewContext(connectionString);
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync($"DROP INDEX \"{VersionIndex}\"");
        return connectionString;
    }

    private async Task DropDatabaseAsync(string connectionString)
    {
        await using (var connection = new NpgsqlConnection(connectionString))
            NpgsqlConnection.ClearPool(connection);
        await using var server = new NpgsqlConnection(fixture.ConnectionString);
        await server.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{new NpgsqlConnectionStringBuilder(connectionString).Database}\" WITH (FORCE)", server);
        await drop.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Holds each call right after it read the runs of the order until all <paramref name="calls"/> calls read them or a second passed,
    /// so without the lock on the order every call passes the checks before any of them inserts.
    /// </summary>
    private sealed class MeetingAfterTheChecks(Guid orderId, int calls)
    {
        private readonly TaskCompletionSource _all = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reached;

        public int Reached => Volatile.Read(ref _reached);

        private Guid OrderId => orderId;

        public IInterceptor ForOneCall() => new OneCall(this);

        private async Task ArriveAsync(CancellationToken ct)
        {
            if (Interlocked.Increment(ref _reached) >= calls)
                _all.TrySetResult();
            try
            {
                await _all.Task.WaitAsync(TimeSpan.FromSeconds(1), ct);
            }
            catch (TimeoutException)
            {
            }
        }

        private sealed class OneCall(MeetingAfterTheChecks meeting) : DbCommandInterceptor
        {
            private int _arrived;

            public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
                DbDataReader result, CancellationToken cancellationToken = default)
            {
                var readsRuns = command.CommandText.Contains("FROM \"Runs\"", StringComparison.Ordinal)
                                && command.Parameters.Cast<DbParameter>().Any(p => p.Value is Guid id && id == meeting.OrderId);
                if (readsRuns && Interlocked.Exchange(ref _arrived, 1) == 0)
                    await meeting.ArriveAsync(cancellationToken);
                return result;
            }
        }
    }

    /// <summary>
    /// Writes the next version of the order of <paramref name="baseRunId"/> inside the same transaction right before the change is
    /// inserted, as if another call had passed the checks too; only the unique index can then stop the second version.
    /// </summary>
    private sealed class VersionWrittenAfterTheChecks(Guid baseRunId) : DbCommandInterceptor
    {
        private int _fired;

        public bool Fired => _fired == 1;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await RaceAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await RaceAsync(command, cancellationToken);
            return result;
        }

        private async Task RaceAsync(DbCommand command, CancellationToken ct)
        {
            if (!command.CommandText.Contains("INSERT INTO \"Runs\"", StringComparison.Ordinal) || Interlocked.Exchange(ref _fired, 1) == 1)
                return;
            await using var other = command.Connection!.CreateCommand();
            other.Transaction = command.Transaction;
            other.CommandText =
                $"""
                CREATE TEMP TABLE race_run ON COMMIT DROP AS SELECT * FROM "Runs" WHERE "Id" = '{baseRunId}';
                UPDATE race_run SET "Id" = gen_random_uuid(), "OrderVersion" = "OrderVersion" + 1, "FinalText" = NULL;
                INSERT INTO "Runs" SELECT * FROM race_run;
                """;
            await other.ExecuteNonQueryAsync(ct);
        }
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
