using System.Collections.Concurrent;
using Geef.Atelier.Application.Dashboard;
using Geef.Atelier.Core.Domain.Dashboard;
using Geef.Atelier.Core.Notifications;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Geef.Atelier.Web.Notifications;

/// <summary>
/// Publishes run-update and dashboard notifications to the in-process <see cref="IRunUpdateFeed"/> (used by the
/// Blazor pages) and to SignalR clients of <see cref="RunHub"/>. The hub has no client inside the application any
/// more and stays only until its removal is decided. Run-list updates go only to the owner and to administrators.
/// </summary>
internal sealed class LiveRunNotifier(
    IHubContext<RunHub> hub,
    IDashboardService dashboardService,
    IRunUpdateFeed feed,
    IServiceScopeFactory scopeFactory,
    ILogger<LiveRunNotifier> logger) : IRunNotifier
{
    private const int OwnerCacheLimit = 10_000;

    private readonly ConcurrentDictionary<Guid, string?> _owners = new();

    /// <inheritdoc/>
    public async Task NotifyRunUpdatedAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var owner = await ResolveOwnerAsync(runId, cancellationToken);
        await SendAsync(RunUpdateTopics.Run(runId), "RunUpdated", runId, cancellationToken);
        await SendAsync(RunUpdateTopics.AdminRuns, "AnyRunUpdated", runId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(owner))
            await SendAsync(RunUpdateTopics.UserRuns(owner), "AnyRunUpdated", runId, cancellationToken);
        await feed.PublishRunUpdatedAsync(runId, owner);
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

    private async Task<string?> ResolveOwnerAsync(Guid runId, CancellationToken cancellationToken)
    {
        if (_owners.TryGetValue(runId, out var cached))
            return cached;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var run = await scope.ServiceProvider.GetRequiredService<IRunRepository>().GetByIdAsync(runId, cancellationToken);
            if (run is null)
                return null;
            if (_owners.Count >= OwnerCacheLimit)
                _owners.Clear();
            _owners[runId] = run.CreatedByUser;
            return run.CreatedByUser;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Owner lookup for run {RunId} failed; list update goes to administrators only", runId);
            return null;
        }
    }
}
