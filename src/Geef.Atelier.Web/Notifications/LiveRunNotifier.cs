using System.Collections.Concurrent;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Notifications;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Orders;

namespace Geef.Atelier.Web.Notifications;

/// <summary>
/// Publishes run updates to the in-process <see cref="IRunUpdateFeed"/> used by the Blazor pages. Run-list updates go only to
/// the owner and to administrators. A run that belongs to an order also updates the order topics, so order pages follow
/// their runs.
/// </summary>
internal sealed class LiveRunNotifier(
    IRunUpdateFeed feed,
    IServiceScopeFactory scopeFactory,
    ILogger<LiveRunNotifier> logger) : IRunNotifier, IOrderNotifier
{
    private const int OwnerCacheLimit = 10_000;

    private readonly ConcurrentDictionary<Guid, RunLink> _runs = new();
    private readonly ConcurrentDictionary<Guid, string?> _orderOwners = new();

    /// <inheritdoc/>
    public async Task NotifyRunUpdatedAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var link = await ResolveRunAsync(runId, cancellationToken);
        var owner = link?.Owner;
        await feed.PublishRunUpdatedAsync(runId, owner);
        if (link?.OrderId is { } orderId)
            await feed.PublishOrderUpdatedAsync(orderId, owner);
    }

    /// <inheritdoc/>
    public async Task NotifyOrderUpdatedAsync(Guid orderId, CancellationToken ct = default)
    {
        var owner = await ResolveOrderOwnerAsync(orderId, ct);
        await feed.PublishOrderUpdatedAsync(orderId, owner);
    }

    private async Task<RunLink?> ResolveRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        if (_runs.TryGetValue(runId, out var cached))
            return cached;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var run = await scope.ServiceProvider.GetRequiredService<IRunRepository>().GetByIdAsync(runId, cancellationToken);
            if (run is null)
                return null;
            var link = new RunLink(run.CreatedByUser, run.OrderId);
            if (link.OrderId is null && run.Kind != RunKind.Learning)
                return link;
            if (_runs.Count >= OwnerCacheLimit)
                _runs.Clear();
            _runs[runId] = link;
            return link;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Owner lookup for run {RunId} failed; list update goes to administrators only", runId);
            return null;
        }
    }

    private async Task<string?> ResolveOrderOwnerAsync(Guid orderId, CancellationToken cancellationToken)
    {
        if (_orderOwners.TryGetValue(orderId, out var cached))
            return cached;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var order = await scope.ServiceProvider.GetRequiredService<IOrderRepository>().GetAsync(orderId, cancellationToken);
            if (order is null)
                return null;
            if (_orderOwners.Count >= OwnerCacheLimit)
                _orderOwners.Clear();
            _orderOwners[orderId] = order.CreatedByUser;
            return order.CreatedByUser;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Owner lookup for order {OrderId} failed; list update goes to administrators only", orderId);
            return null;
        }
    }

    private sealed record RunLink(string? Owner, Guid? OrderId);
}
