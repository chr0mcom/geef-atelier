using System.Security.Claims;
using Geef.Atelier.Web.Notifications;

namespace Geef.Atelier.Tests.Web.Components.Orders;

/// <summary>Feed that hands the order handlers to the test; run and dashboard members are inert.</summary>
internal sealed class ScriptedOrderFeed : IRunUpdateFeed
{
    public Func<Guid, Task>? OrderHandler { get; private set; }
    public Func<Guid, Task>? ListHandler { get; private set; }
    public int OrderSubscriptions { get; private set; }
    public int OrderDisposals { get; private set; }
    public int ListSubscriptions { get; private set; }
    public int ListDisposals { get; private set; }
    public TaskCompletionSource? WatchHold { get; set; }
    public bool Deny { get; set; }

    public async Task<IDisposable?> WatchOrderAsync(ClaimsPrincipal user, Guid orderId, Func<Guid, Task> onUpdated, CancellationToken ct = default)
    {
        if (WatchHold is { } hold)
            await hold.Task;
        if (Deny)
            return null;
        OrderHandler = onUpdated;
        OrderSubscriptions++;
        return new Release(() => OrderDisposals++);
    }

    public IDisposable WatchOrderList(ClaimsPrincipal user, Func<Guid, Task> onUpdated)
    {
        ListHandler = onUpdated;
        ListSubscriptions++;
        return new Release(() => ListDisposals++);
    }

    public Task PublishOrderUpdatedAsync(Guid orderId, string? owner) => Task.CompletedTask;

    public Task<IDisposable?> WatchRunAsync(ClaimsPrincipal user, Guid runId, Func<Guid, Task> onUpdated, CancellationToken ct = default) =>
        Task.FromResult<IDisposable?>(null);

    public IDisposable WatchRunList(ClaimsPrincipal user, Func<Guid, Task> onUpdated) => new Release(() => { });

    public Task PublishRunUpdatedAsync(Guid runId, string? owner) => Task.CompletedTask;

    private sealed class Release(Action onDispose) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                onDispose();
        }
    }
}
