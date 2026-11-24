using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Security.Claims;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain.Dashboard;

namespace Geef.Atelier.Web.Notifications;

/// <summary>
/// Topic-based <see cref="IRunUpdateFeed"/> held in memory. Publishing never waits for subscribers: each subscription
/// has its own queue, runs its handler one payload at a time (components reload on a single DbContext), skips a payload
/// that is already waiting, and logs handler failures.
/// <para>
/// Limit: access is decided once, when a page subscribes, from the principal of its circuit. A role taken away or a
/// sign-in that expires while the circuit stays open takes effect only when the page subscribes again or the circuit
/// ends; until then an administrator's dashboard subscription keeps receiving the dashboard events of all users
/// (day-book entries, press state), and run subscriptions keep receiving run ids. Checking again on every event would
/// not help: the circuit's principal is a snapshot of the sign-in cookie and does not change, because the admin role
/// is written into the cookie at sign-in. The SignalR hub, in contrast, closes connections when the sign-in expires
/// (<see cref="LiveUpdateServiceExtensions.MapRunHub"/>). Circuits are short-lived, which keeps the exposure small.
/// </para>
/// </summary>
internal sealed class InProcessRunUpdateFeed(
    IServiceScopeFactory scopeFactory,
    ILogger<InProcessRunUpdateFeed> logger) : IRunUpdateFeed
{
    private readonly ConcurrentDictionary<string, ImmutableList<Subscription>> _topics = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public async Task<IDisposable?> WatchRunAsync(ClaimsPrincipal user, Guid runId, Func<Guid, Task> onUpdated, CancellationToken ct = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var runService = scope.ServiceProvider.GetRequiredService<IRunService>();
            if (!await runService.CanWatchRunAsync(user, runId, ct))
                return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        if (ct.IsCancellationRequested)
            return null;
        return Add(RunUpdateTopics.Run(runId), payload => payload is Guid id ? onUpdated(id) : Task.CompletedTask);
    }

    /// <inheritdoc/>
    public IDisposable WatchRunList(ClaimsPrincipal user, Func<Guid, Task> onUpdated) =>
        user.RunListTopic() is { } topic
            ? Add(topic, payload => payload is Guid id ? onUpdated(id) : Task.CompletedTask)
            : Subscription.None;

    /// <inheritdoc/>
    public IDisposable WatchDashboard(ClaimsPrincipal user, bool allUsers, Func<DashboardEvent, Task> onEvent)
    {
        string? topic = allUsers && user.IsAtelierAdmin()
            ? RunUpdateTopics.AdminDashboard
            : user.WatcherName() is { } name ? RunUpdateTopics.UserDashboard(name) : null;
        return topic is null
            ? Subscription.None
            : Add(topic, payload => payload is DashboardEvent evt ? onEvent(evt) : Task.CompletedTask);
    }

    /// <inheritdoc/>
    public Task PublishRunUpdatedAsync(Guid runId, string? owner)
    {
        var topics = new List<string> { RunUpdateTopics.Run(runId), RunUpdateTopics.AdminRuns };
        if (!string.IsNullOrWhiteSpace(owner))
            topics.Add(RunUpdateTopics.UserRuns(owner));
        return PublishAsync(topics, runId);
    }

    /// <inheritdoc/>
    public Task PublishDashboardEventAsync(DashboardEvent dashboardEvent)
    {
        var topics = new List<string> { RunUpdateTopics.AdminDashboard };
        if (!string.IsNullOrWhiteSpace(dashboardEvent.Username))
            topics.Add(RunUpdateTopics.UserDashboard(dashboardEvent.Username));
        return PublishAsync(topics, dashboardEvent);
    }

    private Task PublishAsync(IEnumerable<string> topics, object payload)
    {
        var subscriptions = topics
            .SelectMany(t => _topics.TryGetValue(t, out var subs) ? subs : ImmutableList<Subscription>.Empty)
            .Distinct();
        foreach (var subscription in subscriptions)
            subscription.Enqueue(payload);
        return Task.CompletedTask;
    }

    private Subscription Add(string topic, Func<object, Task> handler)
    {
        var subscription = new Subscription(topic, handler, Remove, logger);
        _topics.AddOrUpdate(topic, _ => [subscription], (_, list) => list.Add(subscription));
        return subscription;
    }

    private void Remove(Subscription subscription)
    {
        while (_topics.TryGetValue(subscription.Topic, out var list))
        {
            var updated = list.Remove(subscription);
            if (updated.IsEmpty
                    ? _topics.TryRemove(new KeyValuePair<string, ImmutableList<Subscription>>(subscription.Topic, list))
                    : _topics.TryUpdate(subscription.Topic, updated, list))
                return;
        }
    }

    private sealed class Subscription(
        string topic,
        Func<object, Task> handler,
        Action<Subscription>? onDispose,
        ILogger? logger) : IDisposable
    {
        public static readonly Subscription None = new(string.Empty, _ => Task.CompletedTask, null, null);

        private readonly object _gate = new();
        private readonly Queue<object> _pending = new();
        private bool _pumping;
        private int _disposed;

        public string Topic { get; } = topic;

        public void Enqueue(object payload)
        {
            lock (_gate)
            {
                if (_disposed == 1 || _pending.Contains(payload))
                    return;
                _pending.Enqueue(payload);
                if (_pumping)
                    return;
                _pumping = true;
            }
            _ = Task.Run(PumpAsync);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            lock (_gate)
                _pending.Clear();
            onDispose?.Invoke(this);
        }

        private async Task PumpAsync()
        {
            while (true)
            {
                object payload;
                lock (_gate)
                {
                    if (_disposed == 1 || !_pending.TryDequeue(out payload!))
                    {
                        _pumping = false;
                        return;
                    }
                }
                try
                {
                    await handler(payload);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Live-update handler for topic {Topic} failed; update skipped for this subscriber", Topic);
                }
            }
        }
    }
}
