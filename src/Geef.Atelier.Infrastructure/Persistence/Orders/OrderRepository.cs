using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Persistence.Orders;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Geef.Atelier.Infrastructure.Persistence.Orders;

internal sealed partial class OrderRepository(AtelierDbContext db, TimeProvider? timeProvider = null) : IOrderRepository
{
    private const string SourceForeignKey = "FK_OrderSources_Sources_SourceId";
    private const string FirstRunIndex = "UX_Runs_OrderId_OrderVersion_Standard";

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

        await InsertAsync(sources, ct, () =>
        {
            db.Orders.Add(order);
            db.OrderSources.AddRange(sources);
            db.Runs.Add(run);
        });
    }

    public async Task CreateAsync(OrderEntity order, IReadOnlyList<OrderSourceEntity> sources, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(sources);
        EnsureLinksBelongTo(order, sources);

        await InsertAsync(sources, ct, () =>
        {
            db.Orders.Add(order);
            db.OrderSources.AddRange(sources);
        });
    }

    private async Task InsertAsync(IReadOnlyList<OrderSourceEntity> sources, CancellationToken ct, Action add)
    {
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            add();
            await db.SaveChangesAsync(ct);
            await ClearExpiryAsync(sources, ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation, ConstraintName: SourceForeignKey })
        {
            throw new OrderSourceMissingException(ex);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<(RevisionStart Result, int? Version)> TryAddRevisionRunAsync(Guid orderId, Guid baseRunId, RunEntity run, int maxVersions, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.OrderId is { } given && given != orderId)
            throw new ArgumentException("The run belongs to another order.", nameof(run));
        if (run.Kind != RunKind.Standard)
            throw new ArgumentException("A change of a text is a standard run.", nameof(run));

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            if (!await LockOrderAsync(orderId, ct))
                return (RevisionStart.NotFound, null);

            var state = await db.Orders.AsNoTracking().Where(o => o.Id == orderId).Select(o => o.State).SingleAsync(ct);
            if (state == OrderState.Cancelled)
                return (RevisionStart.Cancelled, null);

            var runs = await SlimRuns(db.Runs.AsNoTracking().Where(r => r.OrderId == orderId)).ToListAsync(ct);
            if (runs.Any(r => r.Status is RunStatus.Pending or RunStatus.Running))
                return (RevisionStart.Busy, null);
            if (OrderStateResolver.SelectShownRun(runs)?.Id != baseRunId)
                return (RevisionStart.BaseNotShown, null);

            var version = runs.Where(r => r.Kind == RunKind.Standard).Max(r => r.OrderVersion ?? 0) + 1;
            if (version > maxVersions)
                return (RevisionStart.TooManyVersions, null);
            if (run.OrderVersion is { } expected && expected != version)
                return (RevisionStart.BaseNotShown, null);

            var entry = db.Runs.Add(run);
            entry.Property(r => r.OrderId).CurrentValue = orderId;
            entry.Property(r => r.OrderVersion).CurrentValue = version;
            await db.SaveChangesAsync(ct);
            await TouchInTransactionAsync(orderId, _time.GetUtcNow(), ct);
            await tx.CommitAsync(ct);
            return (RevisionStart.Created, version);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: FirstRunIndex })
        {
            return (RevisionStart.Busy, null);
        }
        finally
        {
            db.ChangeTracker.Clear();
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
        var runs = await SlimRuns(db.Runs.AsNoTracking().Where(r => r.OrderId != null && ids.Contains(r.OrderId.Value))).ToListAsync(ct);
        var counts = await db.OrderSources.AsNoTracking()
            .Where(l => ids.Contains(l.OrderId))
            .GroupBy(l => l.OrderId)
            .Select(g => new { OrderId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.OrderId, x => x.Count, ct);

        var runsByOrder = runs.ToLookup(r => r.OrderId!.Value);
        return orders
            .Select(o =>
            {
                var ofOrder = runsByOrder[o.Id].ToList();
                return new OrderListRow(o, OrderStateResolver.SelectCurrentRun(ofOrder), counts.GetValueOrDefault(o.Id), OrderStateResolver.SelectShownRun(ofOrder));
            })
            .ToList();
    }

    /// <summary>
    /// Slim copies of runs for lists and checks: <see cref="RunEntity.FinalText"/> is <see cref="TextMarker"/> when a text exists; the
    /// report is loaded only for a change of an earlier version (<see cref="RunEntity.RevisionHint"/> set), whose display state depends on it.
    /// </summary>
    private static IQueryable<RunEntity> SlimRuns(IQueryable<RunEntity> runs) =>
        runs.Select(r => new RunEntity
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
            RevisionHint = r.RevisionHint,
            ReportJson = r.RevisionHint != null ? r.ReportJson : null,
        });

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

    public async Task<IReadOnlyList<Guid>> ListIntakeCandidatesAsync(DateTimeOffset staleBefore, int maxAttempts, int limit, CancellationToken ct = default)
    {
        if (limit <= 0)
            return [];

        return await ClaimableForIntake(staleBefore, maxAttempts)
            .OrderBy(o => o.CreatedAt)
            .ThenBy(o => o.Id)
            .Select(o => o.Id)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<bool> TryClaimForIntakeAsync(Guid orderId, DateTimeOffset now, DateTimeOffset staleBefore, int maxAttempts, CancellationToken ct = default)
    {
        var affected = await ClaimableForIntake(staleBefore, maxAttempts)
            .Where(o => o.Id == orderId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.ClaimedAt, now)
                .SetProperty(o => o.IntakeStartedAt, now)
                .SetProperty(o => o.IntakeAttempts, o => o.IntakeAttempts + 1), ct);
        return affected > 0;
    }

    public async Task<IReadOnlyList<Guid>> ListStuckIntakesAsync(DateTimeOffset staleBefore, DateTimeOffset unclaimedBefore, int maxAttempts, int limit, CancellationToken ct = default)
    {
        if (limit <= 0)
            return [];

        return await db.Orders.AsNoTracking()
            .Where(o => o.State == OrderState.Analyzing
                        && ((o.IntakeAttempts >= maxAttempts && (o.ClaimedAt == null || o.ClaimedAt < staleBefore))
                            || (o.ClaimedAt == null && o.CreatedAt < unclaimedBefore)))
            .OrderBy(o => o.CreatedAt)
            .ThenBy(o => o.Id)
            .Select(o => o.Id)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<bool> TrySaveQuestionsAsync(Guid orderId, string clarifyJson, DateTimeOffset at, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clarifyJson);
        var affected = await db.Orders
            .Where(o => o.Id == orderId && o.State == OrderState.Analyzing)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.State, OrderState.NeedsInput)
                .SetProperty(o => o.ClarifyJson, clarifyJson)
                .SetProperty(o => o.IntakeOutcome, IntakeOutcome.Asked)
                .SetProperty(o => o.IntakeFinishedAt, at)
                .SetProperty(o => o.QuestionsAskedAt, at)
                .SetProperty(o => o.UpdatedAt, at), ct);
        return affected > 0;
    }

    public async Task<bool> TryStartWithRunAsync(OrderStart start, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(start.Run);
        ArgumentNullException.ThrowIfNull(start.RoleChanges);
        if (start.Run.OrderId != start.OrderId || start.Run.OrderVersion != 1 || start.Run.Kind != RunKind.Standard)
            throw new ArgumentException("The first run must be a standard run of the order with version 1.", nameof(start));

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            if (!await LockOrderAsync(start.OrderId, ct))
                return false;

            var state = await db.Orders.AsNoTracking().Where(o => o.Id == start.OrderId).Select(o => o.State).SingleAsync(ct);
            if (state != start.Expected)
                return false;
            if (await db.Runs.AnyAsync(r => r.OrderId == start.OrderId && r.Kind == RunKind.Standard && r.OrderVersion == 1, ct))
                return false;

            var fromAnalyzing = start.Expected == OrderState.Analyzing;
            var fromNeedsInput = start.Expected == OrderState.NeedsInput;
            await db.Orders
                .Where(o => o.Id == start.OrderId)
                .ExecuteUpdateAsync(s =>
                {
                    s.SetProperty(o => o.State, OrderState.Ready);
                    s.SetProperty(o => o.IntakeOutcome, start.Outcome);
                    s.SetProperty(o => o.UpdatedAt, start.At);
                    if (start.ClarifyJson is not null)
                        s.SetProperty(o => o.ClarifyJson, start.ClarifyJson);
                    if (start.AnswersJson is not null)
                        s.SetProperty(o => o.AnswersJson, start.AnswersJson);
                    if (fromAnalyzing)
                        s.SetProperty(o => o.IntakeFinishedAt, start.At);
                    if (fromNeedsInput)
                        s.SetProperty(o => o.AnsweredAt, start.At);
                }, ct);

            foreach (var (sourceId, role) in start.RoleChanges)
                await db.OrderSources
                    .Where(l => l.OrderId == start.OrderId && l.SourceId == sourceId)
                    .ExecuteUpdateAsync(s => s.SetProperty(l => l.Role, role), ct);

            db.Runs.Add(start.Run);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: FirstRunIndex })
        {
            return false;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<bool> TrySetResearchAsync(Guid orderId, string researchJson, DateTimeOffset? at, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(researchJson);
        var when = at?.ToUniversalTime();
        var affected = await db.Orders
            .Where(o => o.Id == orderId && o.ResearchJson == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.ResearchJson, researchJson)
                .SetProperty(o => o.ResearchAt, o => o.ResearchAt ?? when), ct);
        return affected > 0;
    }

    /// <summary>Key of the transaction lock that puts the reservations of searches in a row.</summary>
    internal const string ResearchLockKey = "geef-atelier-research-reservation";

    public async Task<bool> TryReserveResearchAsync(Guid orderId, string? user, DateTimeOffset at, DateTimeOffset since, int dailyCap, int userCap, CancellationToken ct = default)
    {
        var owner = user ?? string.Empty;
        var now = at.ToUniversalTime();
        var from = since.ToUniversalTime();
        var to = from.AddDays(1);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({ResearchLockKey}, 0))", ct);
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Orders" SET "ResearchAt" = {now}
            WHERE "Id" = {orderId} AND "ResearchJson" IS NULL
              AND (SELECT count(*) FROM "Orders" WHERE "ResearchAt" >= {from} AND "ResearchAt" < {to} AND "Id" <> {orderId}) < {dailyCap}
              AND (SELECT count(*) FROM "Orders" WHERE "ResearchAt" >= {from} AND "ResearchAt" < {to} AND "Id" <> {orderId} AND COALESCE("CreatedByUser", '') = {owner}) < {userCap}
            """, ct);
        await transaction.CommitAsync(ct);
        return affected > 0;
    }

    public Task<int> CountResearchSinceAsync(DateTimeOffset since, CancellationToken ct = default)
    {
        var from = since.ToUniversalTime();
        return db.Orders.AsNoTracking().CountAsync(o => o.ResearchAt != null && o.ResearchAt >= from, ct);
    }

    public async Task<IReadOnlyList<Guid>> ListUnansweredAsync(DateTimeOffset askedBefore, int limit, CancellationToken ct = default)
    {
        if (limit <= 0)
            return [];

        return await db.Orders.AsNoTracking()
            .Where(o => o.State == OrderState.NeedsInput && o.QuestionsAskedAt != null && o.QuestionsAskedAt < askedBefore)
            .OrderBy(o => o.QuestionsAskedAt)
            .ThenBy(o => o.Id)
            .Select(o => o.Id)
            .Take(limit)
            .ToListAsync(ct);
    }

    private IQueryable<OrderEntity> ClaimableForIntake(DateTimeOffset staleBefore, int maxAttempts) =>
        db.Orders.Where(o =>
            o.State == OrderState.Analyzing
            && o.IntakeAttempts < maxAttempts
            && (o.ClaimedAt == null || o.ClaimedAt < staleBefore));

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
