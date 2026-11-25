using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Persistence.Intake;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.Intake;

internal sealed class IntakeShadowRepository(AtelierDbContext db) : IIntakeShadowRepository
{
    private static readonly string[] CountedStatuses = [IntakeShadowStatuses.Done, IntakeShadowStatuses.Failed, IntakeShadowStatuses.TimedOut];

    public async Task AddPendingAsync(Guid orderId, DateTimeOffset at, CancellationToken ct = default) =>
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "OrderIntakeShadows" ("OrderId", "Status", "Attempts", "CreatedAt")
            VALUES ({orderId}, {IntakeShadowStatuses.Pending}, 0, {at})
            ON CONFLICT ("OrderId") DO NOTHING
            """, ct);

    public async Task<IReadOnlyList<Guid>> ListPendingAsync(DateTimeOffset staleBefore, int maxAttempts, int limit, CancellationToken ct = default)
    {
        if (limit <= 0)
            return [];

        return await Claimable(staleBefore, maxAttempts)
            .OrderBy(s => s.CreatedAt)
            .ThenBy(s => s.OrderId)
            .Select(s => s.OrderId)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<bool> TryClaimAsync(Guid orderId, DateTimeOffset now, DateTimeOffset staleBefore, int maxAttempts, CancellationToken ct = default)
    {
        var affected = await Claimable(staleBefore, maxAttempts)
            .Where(s => s.OrderId == orderId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.ClaimedAt, now)
                .SetProperty(x => x.Attempts, x => x.Attempts + 1), ct);
        return affected > 0;
    }

    public async Task CompleteAsync(Guid orderId, string status, int? durationMs, int? questionCount, string? recordJson, DateTimeOffset at, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        await db.OrderIntakeShadows
            .Where(s => s.OrderId == orderId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, status)
                .SetProperty(x => x.DurationMs, durationMs)
                .SetProperty(x => x.QuestionCount, questionCount)
                .SetProperty(x => x.RecordJson, recordJson)
                .SetProperty(x => x.CompletedAt, at), ct);
    }

    public Task<int> CountCompletedSinceAsync(DateTimeOffset since, CancellationToken ct = default) =>
        db.OrderIntakeShadows.CountAsync(s => s.CompletedAt != null && s.CompletedAt >= since && CountedStatuses.Contains(s.Status), ct);

    private IQueryable<OrderIntakeShadowEntity> Claimable(DateTimeOffset staleBefore, int maxAttempts) =>
        db.OrderIntakeShadows.Where(s =>
            s.Status == IntakeShadowStatuses.Pending
            && s.Attempts < maxAttempts
            && (s.ClaimedAt == null || s.ClaimedAt < staleBefore));
}
