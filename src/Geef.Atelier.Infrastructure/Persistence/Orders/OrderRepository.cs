using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Persistence.Orders;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.Orders;

internal sealed partial class OrderRepository(AtelierDbContext db, TimeProvider? timeProvider = null) : IOrderRepository
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>Stands in for the final text in the slim runs of <see cref="ListAsync"/>, which only need to know whether a text exists.</summary>
    internal const string TextMarker = "…";

    public async Task CreateWithFirstRunAsync(OrderEntity order, IReadOnlyList<OrderSourceEntity> sources, RunEntity run, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(run);
        if (run.OrderId != order.Id || run.OrderVersion != 1)
            throw new ArgumentException("The first run must belong to the order and carry version 1.", nameof(run));
        EnsureLinksBelongTo(order, sources);

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            db.Orders.Add(order);
            db.OrderSources.AddRange(sources);
            db.Runs.Add(run);
            await db.SaveChangesAsync(ct);
            await ClearExpiryAsync(sources, ct);
            await tx.CommitAsync(ct);
            db.ChangeTracker.Clear();
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task CreateAsync(OrderEntity order, IReadOnlyList<OrderSourceEntity> sources, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(sources);
        EnsureLinksBelongTo(order, sources);

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            db.Orders.Add(order);
            db.OrderSources.AddRange(sources);
            await db.SaveChangesAsync(ct);
            await ClearExpiryAsync(sources, ct);
            await tx.CommitAsync(ct);
            db.ChangeTracker.Clear();
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<int> AddRunAsync(Guid orderId, RunEntity run, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.OrderId is { } given && given != orderId)
            throw new ArgumentException("The run belongs to another order.", nameof(run));

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            if (!await LockOrderAsync(orderId, ct))
                throw new InvalidOperationException($"Order {orderId} not found.");

            var version = run.Kind == RunKind.CrewComposition && run.OrderVersion is { } kept
                ? kept
                : await NextStandardVersionAsync(orderId, ct);

            var entry = db.Runs.Add(run);
            entry.Property(r => r.OrderId).CurrentValue = orderId;
            entry.Property(r => r.OrderVersion).CurrentValue = version;
            await db.SaveChangesAsync(ct);
            await TouchInTransactionAsync(orderId, _time.GetUtcNow(), ct);
            await tx.CommitAsync(ct);
            db.ChangeTracker.Clear();
            return version;
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public Task<OrderEntity?> GetAsync(Guid orderId, CancellationToken ct = default) =>
        db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct);

    public async Task<IReadOnlyList<RunEntity>> ListRunsAsync(Guid orderId, CancellationToken ct = default) =>
        await db.Runs.AsNoTracking()
            .Where(r => r.OrderId == orderId)
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<OrderListRow>> ListAsync(string? ownerUsername, int limit, CancellationToken ct = default)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be positive.");

        var orders = await db.Orders.AsNoTracking()
            .Where(o => ownerUsername == null || o.CreatedByUser == ownerUsername)
            .OrderByDescending(o => o.UpdatedAt)
            .ThenByDescending(o => o.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
        if (orders.Count == 0)
            return [];

        var ids = orders.Select(o => o.Id).ToList();
        var runs = await db.Runs.AsNoTracking()
            .Where(r => r.OrderId != null && ids.Contains(r.OrderId.Value))
            .Select(r => new RunEntity
            {
                Id = r.Id,
                CreatedAt = r.CreatedAt,
                Status = r.Status,
                BriefingText = string.Empty,
                ConfigJson = string.Empty,
                CompletedAt = r.CompletedAt,
                FinalText = r.FinalText == null || r.FinalText.Trim() == string.Empty ? null : TextMarker,
                WordCount = r.WordCount,
                Kind = r.Kind,
                Outcome = r.Outcome,
                OrderId = r.OrderId,
                OrderVersion = r.OrderVersion,
            })
            .ToListAsync(ct);
        var counts = await db.OrderSources.AsNoTracking()
            .Where(l => ids.Contains(l.OrderId))
            .GroupBy(l => l.OrderId)
            .Select(g => new { OrderId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.OrderId, x => x.Count, ct);

        var runsByOrder = runs.ToLookup(r => r.OrderId!.Value);
        return orders
            .Select(o => new OrderListRow(
                o,
                OrderStateResolver.SelectCurrentRun(runsByOrder[o.Id].ToList()),
                counts.GetValueOrDefault(o.Id)))
            .ToList();
    }

    public async Task<IReadOnlyList<RunEventRow>> ListRunEventsAsync(Guid runId, CancellationToken ct = default)
    {
        var rows = await db.Database.SqlQuery<RunEventRaw>($"""
            SELECT "EventType", CASE WHEN jsonb_typeof("PayloadJson"->'Iteration') = 'number'
                                     THEN ("PayloadJson"->>'Iteration')::integer END AS "Iteration",
                   "CreatedAt"
            FROM "Events" WHERE "RunId" = {runId}
            ORDER BY "CreatedAt", "Id"
            """).ToListAsync(ct);
        return rows.Select(r => new RunEventRow(r.EventType, r.Iteration, r.CreatedAt)).ToList();
    }

    public async Task<bool> TrySetStateAsync(Guid orderId, OrderState expected, OrderState next, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        var affected = await db.Orders
            .Where(o => o.Id == orderId && o.State == expected)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.State, next)
                .SetProperty(o => o.UpdatedAt, now), ct);
        return affected > 0;
    }

    public async Task<(Guid OrderId, int? Version)?> GetLinkForRunAsync(Guid runId, CancellationToken ct = default)
    {
        var link = await db.Runs.AsNoTracking()
            .Where(r => r.Id == runId && r.OrderId != null)
            .Select(r => new { OrderId = r.OrderId!.Value, r.OrderVersion })
            .FirstOrDefaultAsync(ct);
        return link is null ? null : (link.OrderId, link.OrderVersion);
    }

    public async Task<Guid> LinkLegacyRunAsync(Guid runId, OrderEntry entry, CancellationToken ct = default)
    {
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var run = await db.Runs.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct)
                      ?? throw new InvalidOperationException($"Run {runId} not found.");
            if (run.OrderId is { } existing)
                return existing;
            if (run.Kind == RunKind.Learning)
                throw new InvalidOperationException("Learning runs do not belong to an order.");

            var parentId = run.ParentCompositionRunId ?? run.ParentRunId;
            var parent = parentId is null
                ? null
                : await db.Runs.AsNoTracking()
                    .Where(r => r.Id == parentId && r.OrderId != null)
                    .Select(r => new { OrderId = r.OrderId!.Value, r.OrderVersion, r.Kind })
                    .FirstOrDefaultAsync(ct);

            Guid orderId;
            int version;
            var now = _time.GetUtcNow();
            if (parent is not null && await LockOrderAsync(parent.OrderId, ct))
            {
                orderId = parent.OrderId;
                version = await InheritedVersionAsync(run, parent.OrderId, parent.OrderVersion, parent.Kind, ct);
            }
            else
            {
                orderId = run.Id;
                version = 1;
                db.Orders.Add(new OrderEntity
                {
                    Id = orderId,
                    CreatedByUser = run.CreatedByUser,
                    RequestText = LegacyRequestText(run),
                    Mode = OrderMode.Careful,
                    State = OrderState.Ready,
                    Entry = entry,
                    CrewTemplateName = run.Kind == RunKind.CrewComposition ? null : run.CrewTemplateName,
                    CreatedAt = run.CreatedAt,
                    UpdatedAt = now,
                });
                await db.SaveChangesAsync(ct);
            }

            await SetLinkAsync(run.Id, orderId, version, ct);
            await TouchInTransactionAsync(orderId, now, ct);
            await tx.CommitAsync(ct);
            db.ChangeTracker.Clear();
            return orderId;
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task TouchAsync(Guid orderId, DateTimeOffset at, CancellationToken ct = default) =>
        await TouchInTransactionAsync(orderId, at, ct);

    public async Task<bool> DeleteAsync(Guid orderId, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var orphaned = db.OrderSources
            .Where(l => l.OrderId == orderId)
            .Select(l => l.SourceId)
            .Where(sourceId => !db.OrderSources.Any(other => other.SourceId == sourceId && other.OrderId != orderId));
        await db.Sources
            .Where(s => orphaned.Contains(s.Id) && !s.InLibrary)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, now), ct);

        var affected = await db.Orders.Where(o => o.Id == orderId).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);
        return affected > 0;
    }

    internal static string LegacyRequestText(RunEntity run)
    {
        if (run.Kind != RunKind.CrewComposition)
            return run.BriefingText;

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(run.ConfigJson) ? "{}" : run.ConfigJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("original_task", out var task)
                && task.ValueKind == JsonValueKind.String
                && !string.IsNullOrEmpty(task.GetString()))
                return task.GetString()!;
        }
        catch (JsonException)
        {
        }

        return ComposePrefix().Replace(run.BriefingText, string.Empty);
    }

    private async Task<int> InheritedVersionAsync(RunEntity run, Guid orderId, int? parentVersion, RunKind parentKind, CancellationToken ct)
    {
        if (run.Kind == RunKind.Standard && run.ParentCompositionRunId is not null && parentKind == RunKind.CrewComposition && parentVersion is { } candidate)
        {
            var taken = await db.Runs.AnyAsync(r => r.OrderId == orderId && r.Kind == RunKind.Standard && r.OrderVersion == candidate, ct);
            if (!taken)
                return candidate;
        }

        return await NextStandardVersionAsync(orderId, ct);
    }

    private async Task<int> NextStandardVersionAsync(Guid orderId, CancellationToken ct)
    {
        var max = await db.Runs
            .Where(r => r.OrderId == orderId && r.Kind == RunKind.Standard)
            .MaxAsync(r => (int?)r.OrderVersion, ct);
        return (max ?? 0) + 1;
    }

    private async Task<bool> LockOrderAsync(Guid orderId, CancellationToken ct)
    {
        var found = await db.Database
            .SqlQuery<Guid>($"""SELECT "Id" AS "Value" FROM "Orders" WHERE "Id" = {orderId} FOR UPDATE""")
            .ToListAsync(ct);
        return found.Count > 0;
    }

    private Task SetLinkAsync(Guid runId, Guid orderId, int version, CancellationToken ct) =>
        db.Runs.Where(r => r.Id == runId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.OrderId, orderId)
                .SetProperty(r => r.OrderVersion, version), ct);

    private Task TouchInTransactionAsync(Guid orderId, DateTimeOffset at, CancellationToken ct) =>
        db.Orders.Where(o => o.Id == orderId && o.UpdatedAt < at)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.UpdatedAt, at), ct);

    private async Task ClearExpiryAsync(IReadOnlyList<OrderSourceEntity> sources, CancellationToken ct)
    {
        if (sources.Count == 0)
            return;
        var ids = sources.Select(s => s.SourceId).ToList();
        await db.Sources.Where(s => ids.Contains(s.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, (DateTimeOffset?)null), ct);
    }

    private static void EnsureLinksBelongTo(OrderEntity order, IReadOnlyList<OrderSourceEntity> sources)
    {
        if (sources.Any(s => s.OrderId != order.Id))
            throw new ArgumentException("Every source link must belong to the order.", nameof(sources));
    }

    [GeneratedRegex(@"^Compose a crew for the following task:\s*", RegexOptions.CultureInvariant)]
    private static partial Regex ComposePrefix();

    private sealed record RunEventRaw(string EventType, int? Iteration, DateTimeOffset CreatedAt);
}
