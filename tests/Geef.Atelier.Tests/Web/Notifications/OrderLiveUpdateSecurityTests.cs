using System.Security.Claims;
using Geef.Atelier.Application.Dashboard;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Dashboard;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Notifications;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Tests.Web.Components.Orders;
using Geef.Atelier.Web.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Tests.Web.Notifications;

/// <summary>
/// Order live updates are only for the owner and administrators, and every update of a run that belongs to an order
/// also reaches the order's watchers. Real feed, real notifier and the real registration; the order and run data come
/// from in-memory fakes because the order tables arrive with P1.
/// </summary>
public sealed class OrderLiveUpdateSecurityTests : IAsyncDisposable
{
    private static readonly ClaimsPrincipal Alice = User("alice");
    private static readonly ClaimsPrincipal Bob = User("bob");
    private static readonly ClaimsPrincipal Admin = User("root", "admin");
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());

    private readonly FakeOrderService _orders = new();
    private readonly FakeRunRepository _runs = new();
    private readonly FakeOrderRepository _orderRepository = new();
    private readonly ServiceProvider _provider;
    private readonly IRunUpdateFeed _feed;
    private readonly Guid _aliceOrder;

    public OrderLiveUpdateSecurityTests()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.ClearProviders());
        services.AddSingleton<IOrderService>(_orders);
        services.AddSingleton<IRunRepository>(_runs);
        services.AddSingleton<IOrderRepository>(_orderRepository);
        services.AddSingleton<IDashboardService, InertDashboardService>();
        services.AddRunLiveUpdates();
        _provider = services.BuildServiceProvider();
        _feed = _provider.GetRequiredService<IRunUpdateFeed>();
        _aliceOrder = _orders.Add("alice", OrderTestData.View(OrderDisplayState.Working)).OrderId;
        _orderRepository.Owners[_aliceOrder] = "alice";
    }

    [Fact]
    public async Task AForeignOrderCannotBeWatched()
    {
        Assert.Null(await _feed.WatchOrderAsync(Bob, _aliceOrder, _ => Task.CompletedTask));
        Assert.Null(await _feed.WatchOrderAsync(Anonymous, _aliceOrder, _ => Task.CompletedTask));
        Assert.Null(await _feed.WatchOrderAsync(Alice, Guid.NewGuid(), _ => Task.CompletedTask));
    }

    [Fact]
    public async Task OwnerAndAdministratorsReceiveUpdatesOfTheOrder()
    {
        var owner = new Inbox();
        var admin = new Inbox();
        using var ownerSubscription = await _feed.WatchOrderAsync(Alice, _aliceOrder, owner.Add);
        using var adminSubscription = await _feed.WatchOrderAsync(Admin, _aliceOrder, admin.Add);

        await _feed.PublishOrderUpdatedAsync(_aliceOrder, "alice");

        Assert.Equal(_aliceOrder, await owner.NextAsync());
        Assert.Equal(_aliceOrder, await admin.NextAsync());
    }

    [Fact]
    public async Task ACancelledWatchLeavesNothingBehind()
    {
        var inbox = new Inbox();

        var subscription = await _feed.WatchOrderAsync(Alice, _aliceOrder, inbox.Add, new CancellationToken(canceled: true));
        await _feed.PublishOrderUpdatedAsync(_aliceOrder, "alice");

        Assert.Null(subscription);
        Assert.False(await inbox.ReceivesAsync());
    }

    [Fact]
    public async Task ListsReceiveOwnOrdersAndAdministratorsReceiveAll()
    {
        var alice = new Inbox();
        var bob = new Inbox();
        var admin = new Inbox();
        var anonymous = new Inbox();
        using var a = _feed.WatchOrderList(Alice, alice.Add);
        using var b = _feed.WatchOrderList(Bob, bob.Add);
        using var r = _feed.WatchOrderList(Admin, admin.Add);
        using var n = _feed.WatchOrderList(Anonymous, anonymous.Add);

        await _feed.PublishOrderUpdatedAsync(_aliceOrder, "alice");

        Assert.Equal(_aliceOrder, await alice.NextAsync());
        Assert.Equal(_aliceOrder, await admin.NextAsync());
        Assert.False(await bob.ReceivesAsync());
        Assert.False(await anonymous.ReceivesAsync());
    }

    [Fact]
    public async Task ARunUpdateOfAnOrderReachesTheOrderWatchers()
    {
        var runId = Guid.NewGuid();
        _runs.Runs[runId] = Run(runId, "alice", _aliceOrder);
        var order = new Inbox();
        var list = new Inbox();
        var bobList = new Inbox();
        using var o = await _feed.WatchOrderAsync(Alice, _aliceOrder, order.Add);
        using var l = _feed.WatchOrderList(Alice, list.Add);
        using var b = _feed.WatchOrderList(Bob, bobList.Add);

        await _provider.GetRequiredService<IRunNotifier>().NotifyRunUpdatedAsync(runId);

        Assert.Equal(_aliceOrder, await order.NextAsync());
        Assert.Equal(_aliceOrder, await list.NextAsync());
        Assert.False(await bobList.ReceivesAsync());
    }

    [Fact]
    public async Task ARunWithoutOrderSendsNoOrderUpdate()
    {
        var runId = Guid.NewGuid();
        _runs.Runs[runId] = Run(runId, "alice", null, RunKind.Learning);
        var list = new Inbox();
        using var l = _feed.WatchOrderList(Admin, list.Add);

        await _provider.GetRequiredService<IRunNotifier>().NotifyRunUpdatedAsync(runId);

        Assert.False(await list.ReceivesAsync());
    }

    [Fact]
    public async Task ARunLinkedToAnOrderLaterIsFollowedOnceLinked()
    {
        var runId = Guid.NewGuid();
        _runs.Runs[runId] = Run(runId, "alice", null);
        var list = new Inbox();
        using var l = _feed.WatchOrderList(Alice, list.Add);
        var notifier = _provider.GetRequiredService<IRunNotifier>();

        await notifier.NotifyRunUpdatedAsync(runId);
        Assert.False(await list.ReceivesAsync());

        _runs.Runs[runId] = Run(runId, "alice", _aliceOrder);
        await notifier.NotifyRunUpdatedAsync(runId);

        Assert.Equal(_aliceOrder, await list.NextAsync());
    }

    [Fact]
    public async Task TheOrderNotifierFindsTheOwnerItself()
    {
        var alice = new Inbox();
        var bob = new Inbox();
        using var a = _feed.WatchOrderList(Alice, alice.Add);
        using var b = _feed.WatchOrderList(Bob, bob.Add);

        await _provider.GetRequiredService<IOrderNotifier>().NotifyOrderUpdatedAsync(_aliceOrder);

        Assert.Equal(_aliceOrder, await alice.NextAsync());
        Assert.False(await bob.ReceivesAsync());
    }

    [Fact]
    public void OneNotifierServesRunsAndOrders()
    {
        Assert.Same(
            _provider.GetRequiredService<IRunNotifier>(),
            _provider.GetRequiredService<IOrderNotifier>());
    }

    [Fact]
    public void ReplacingTheRunNotifierKeepsTheOrderNotifierWorking()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDashboardService, InertDashboardService>();
        services.AddRunLiveUpdates();
        services.AddSingleton<IRunNotifier, NoOpRunNotifier>();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<NoOpRunNotifier>(provider.GetRequiredService<IRunNotifier>());
        Assert.NotNull(provider.GetRequiredService<IOrderNotifier>());
    }

    [Fact]
    public void TopicsAreDistinctFromRunTopics()
    {
        var id = Guid.NewGuid();

        Assert.Equal($"order-{id}", RunUpdateTopics.Order(id));
        Assert.Equal("orders-user-alice", RunUpdateTopics.UserOrders("alice"));
        Assert.Equal("orders-admin", RunUpdateTopics.AdminOrders);
        Assert.NotEqual(RunUpdateTopics.Run(id), RunUpdateTopics.Order(id));
    }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

    private static ClaimsPrincipal User(string name, string? role = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, name) };
        if (role is not null)
            claims.Add(new Claim(ClaimTypes.Role, role));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static RunEntity Run(Guid id, string owner, Guid? orderId, RunKind kind = RunKind.Standard) => new()
    {
        Id = id,
        CreatedAt = DateTimeOffset.UtcNow,
        Status = RunStatus.Running,
        BriefingText = "b",
        ConfigJson = "{}",
        CreatedByUser = owner,
        Kind = kind,
        OrderId = orderId,
        OrderVersion = orderId is null ? null : 1,
    };

    private sealed class Inbox
    {
        private readonly System.Threading.Channels.Channel<Guid> _items = System.Threading.Channels.Channel.CreateUnbounded<Guid>();

        public Task Add(Guid id) => _items.Writer.WriteAsync(id).AsTask();

        public async Task<Guid> NextAsync() => await _items.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        public async Task<bool> ReceivesAsync()
        {
            await Task.Delay(200);
            return _items.Reader.TryRead(out _);
        }
    }

    private sealed class FakeRunRepository : IRunRepository
    {
        public Dictionary<Guid, RunEntity> Runs { get; } = [];

        public Task<RunEntity?> GetByIdAsync(Guid runId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Runs.TryGetValue(runId, out var run) ? run : null);

        public Task<IReadOnlyList<RunEntity>> ListAsync(int limit, RunStatus? statusFilter, string? username, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RequestCancellationAsync(Guid runId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RunDetails?> GetDetailsAsync(Guid runId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WelcomeStats> GetWelcomeStatsAsync(string? username, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid runId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeOrderRepository : IOrderRepository
    {
        public Dictionary<Guid, string> Owners { get; } = [];

        public Task<OrderEntity?> GetAsync(Guid orderId, CancellationToken ct = default) =>
            Task.FromResult(Owners.TryGetValue(orderId, out var owner)
                ? new OrderEntity
                {
                    Id = orderId, CreatedByUser = owner, RequestText = "r", Mode = OrderMode.Direct, State = OrderState.Ready,
                    Entry = OrderEntry.Web, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
                }
                : null);

        public Task CreateWithFirstRunAsync(OrderEntity order, IReadOnlyList<OrderSourceEntity> sources, RunEntity run, CancellationToken ct = default) => throw new NotSupportedException();
        public Task CreateAsync(OrderEntity order, IReadOnlyList<OrderSourceEntity> sources, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> AddRunAsync(Guid orderId, RunEntity run, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<RunEntity>> ListRunsAsync(Guid orderId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrderListRow>> ListAsync(string? ownerUsername, int limit, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<RunEventRow>> ListRunEventsAsync(Guid runId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> TrySetStateAsync(Guid orderId, OrderState expected, OrderState next, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<(Guid OrderId, int? Version)?> GetLinkForRunAsync(Guid runId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Guid> LinkLegacyRunAsync(Guid runId, OrderEntry entry, CancellationToken ct = default) => throw new NotSupportedException();
        public Task TouchAsync(Guid orderId, DateTimeOffset at, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid orderId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class InertDashboardService : IDashboardService
    {
        public Task<DashboardSnapshot> GetSnapshotAsync(string username, bool isAdmin, DashboardScope scope, CancellationToken ct = default) => throw new NotSupportedException();
        public void InvalidateUser(string username) { }
        public void InvalidateAdmin() { }
    }
}
