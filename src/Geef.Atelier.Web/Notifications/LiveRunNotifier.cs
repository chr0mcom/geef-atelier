using System.Collections.Concurrent;
using Geef.Atelier.Application.Dashboard;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Dashboard;
using Geef.Atelier.Core.Notifications;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Geef.Atelier.Web.Notifications;

/// <summary>
/// Publishes run-update and dashboard notifications to the in-process <see cref="IRunUpdateFeed"/> (used by the
/// Blazor pages) and to SignalR clients of <see cref="RunHub"/>. The hub has no client inside the application any
/// more. No external clients are known; removal is planned for stage 8. Run-list updates go only to the owner and to
/// administrators. A run that belongs to an order also updates the order topics, so order pages follow their runs.
/// </summary>
internal sealed class LiveRunNotifier(
    IHubContext<RunHub> hub,
    IDashboardService dashboardService,
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
        await SendAsync(RunUpdateTopics.Run(runId), "RunUpdated", runId, cancellationToken);
        await SendAsync(RunUpdateTopics.AdminRuns, "AnyRunUpdated", runId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(owner))
            await SendAsync(RunUpdateTopics.UserRuns(owner), "AnyRunUpdated", runId, cancellationToken);
        await feed.PublishRunUpdatedAsync(runId, owner);
        if (link?.OrderId is { } orderId)
            await PublishOrderAsync(orderId, owner, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task NotifyOrderUpdatedAsync(Guid orderId, CancellationToken ct = default)
    {
        var owner = await ResolveOrderOwnerAsync(orderId, ct);
        await PublishOrderAsync(orderId, owner, ct);
    }

    /// <inheritdoc/>
    public async Task NotifyDashboardEventAsync(DashboardEvent dashboardEvent, CancellationToken cancellationToken = default)
    {
        if (dashboardEvent.Username is { } u)
            dashboardService.InvalidateUser(u);
        dashboardService.InvalidateAdmin();

        if (dashboardEvent.Username is { } username)
            await SendAsync(RunUpdateTopics.UserDashboard(username), "DashboardUpdated", dashboardEvent, cancellationToken);
        await SendAsync(RunUpdateTopics.AdminDashboard, "DashboardUpdated", dashboardEvent, cancellationToken);
        await feed.PublishDashboardEventAsync(dashboardEvent);
    }

    private async Task SendAsync(string group, string method, object payload, CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.Group(group).SendAsync(method, payload, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "SignalR send {Method} to group {Group} failed", method, group);
        }
    }

    private async Task PublishOrderAsync(Guid orderId, string? owner, CancellationToken cancellationToken)
    {
        await SendAsync(RunUpdateTopics.Order(orderId), "OrderUpdated", orderId, cancellationToken);
        await SendAsync(RunUpdateTopics.AdminOrders, "AnyOrderUpdated", orderId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(owner))
            await SendAsync(RunUpdateTopics.UserOrders(owner), "AnyOrderUpdated", orderId, cancellationToken);
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
            var orders = scope.ServiceProvider.GetService<IOrderRepository>();
            if (orders is null)
                return null;
            var order = await orders.GetAsync(orderId, cancellationToken);
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
