using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Persistence.Sources;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.Sources;

internal sealed class UploadTicketRepository(AtelierDbContext db) : IUploadTicketRepository
{
    private static readonly TimeSpan Day = TimeSpan.FromHours(24);

    public async Task AddAsync(UploadTicketEntity ticket, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        db.UploadTickets.Add(ticket);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<bool> AddWithinLimitsAsync(UploadTicketEntity ticket, int maxOpen, int maxPerDay, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var lockKey = "upload-ticket:" + ticket.OwnerUsername;
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({lockKey}))", ct);
            var (open, last24h) = await CountForOwnerAsync(ticket.OwnerUsername, ticket.CreatedAt, ct);
            if (open >= maxOpen || last24h >= maxPerDay)
                return false;

            db.UploadTickets.Add(ticket);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<(int Open, int Last24h)> CountForOwnerAsync(string owner, DateTimeOffset now, CancellationToken ct = default)
    {
        var since = now - Day;
        var counts = await db.UploadTickets.AsNoTracking()
            .Where(t => t.OwnerUsername == owner)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Open = g.Count(t => (t.State == UploadTicketState.Open || t.State == UploadTicketState.Receiving) && t.ExpiresAt > now),
                Last24h = g.Count(t => t.CreatedAt > since),
            })
            .FirstOrDefaultAsync(ct);
        return counts is null ? (0, 0) : (counts.Open, counts.Last24h);
    }

    public async Task<UploadTicketEntity?> TryClaimByTokenAsync(Guid id, string tokenHash, DateTimeOffset now, int maxAttempts, CancellationToken ct = default)
    {
        var claimed = await db.UploadTickets
            .Where(t => t.Id == id && t.TokenHash == tokenHash && t.State == UploadTicketState.Open && t.TokenExpiresAt > now
                        && t.ExpiresAt > now && t.Attempts < maxAttempts)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.State, UploadTicketState.Receiving)
                .SetProperty(t => t.Attempts, t => t.Attempts + 1)
                .SetProperty(t => t.ReceivingSince, now), ct);
        return claimed == 1 ? await ReadAsync(id, ct) : null;
    }

    public async Task<UploadTicketEntity?> TryClaimByOwnerAsync(Guid id, string owner, DateTimeOffset now, int maxAttempts, CancellationToken ct = default)
    {
        var claimed = await db.UploadTickets
            .Where(t => t.Id == id && t.OwnerUsername == owner && t.State == UploadTicketState.Open && t.ExpiresAt > now
                        && t.Attempts < maxAttempts)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.State, UploadTicketState.Receiving)
                .SetProperty(t => t.Attempts, t => t.Attempts + 1)
                .SetProperty(t => t.ReceivingSince, now), ct);
        return claimed == 1 ? await ReadAsync(id, ct) : null;
    }

    public async Task<UploadClaimProbe> ProbeAsync(Guid id, string owner, DateTimeOffset now, CancellationToken ct = default)
    {
        var ticket = await db.UploadTickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id && t.OwnerUsername == owner, ct);
        return ticket is null ? UploadClaimProbe.NotFound : Probe(ticket, ticket.ExpiresAt, now);
    }

    public async Task<UploadClaimProbe> ProbeByTokenAsync(Guid id, string tokenHash, DateTimeOffset now, CancellationToken ct = default)
    {
        var ticket = await db.UploadTickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id && t.TokenHash == tokenHash, ct);
        return ticket is null ? UploadClaimProbe.NotFound : Probe(ticket, ticket.TokenExpiresAt < ticket.ExpiresAt ? ticket.TokenExpiresAt : ticket.ExpiresAt, now);
    }

    public async Task<bool> CompleteAsync(Guid id, DateTimeOffset receivingSince, Guid sourceId, DateTimeOffset now, CancellationToken ct = default) =>
        await db.UploadTickets
            .Where(t => t.Id == id && t.State == UploadTicketState.Receiving && t.ReceivingSince == receivingSince)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.State, UploadTicketState.Used)
                .SetProperty(t => t.SourceId, sourceId)
                .SetProperty(t => t.UsedAt, now)
                .SetProperty(t => t.ReceivingSince, (DateTimeOffset?)null), ct) == 1;

    public async Task ReleaseAsync(Guid id, DateTimeOffset receivingSince, int maxAttempts, string? checkedRefusalCode, CancellationToken ct = default)
    {
        var claim = db.UploadTickets.Where(t => t.Id == id && t.State == UploadTicketState.Receiving && t.ReceivingSince == receivingSince);
        if (checkedRefusalCode is null)
        {
            await claim.ExecuteUpdateAsync(s => s
                .SetProperty(t => t.State, UploadTicketState.Open)
                .SetProperty(t => t.Attempts, t => t.Attempts > 0 ? t.Attempts - 1 : 0)
                .SetProperty(t => t.ReceivingSince, (DateTimeOffset?)null), ct);
            return;
        }

        await ReleaseWhereAsync(claim, maxAttempts, ct, checkedRefusalCode);
    }

    public async Task<IReadOnlyList<UploadTicketEntity>> FindForOwnerAsync(IReadOnlyCollection<Guid> ids, string owner, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
            return [];
        var wanted = ids.Distinct().ToList();
        return await db.UploadTickets.AsNoTracking()
            .Where(t => wanted.Contains(t.Id) && t.OwnerUsername == owner)
            .ToListAsync(ct);
    }

    public Task<int> ResetStaleReceivingAsync(DateTimeOffset olderThan, int maxAttempts, CancellationToken ct = default) =>
        ReleaseWhereAsync(db.UploadTickets.Where(t => t.State == UploadTicketState.Receiving
                                                      && (t.ReceivingSince == null || t.ReceivingSince < olderThan)), maxAttempts, ct);

    public async Task<bool> ResetStaleReceivingAsync(Guid id, DateTimeOffset olderThan, int maxAttempts, CancellationToken ct = default) =>
        await ReleaseWhereAsync(db.UploadTickets.Where(t => t.Id == id && t.State == UploadTicketState.Receiving
                                                            && (t.ReceivingSince == null || t.ReceivingSince < olderThan)), maxAttempts, ct) > 0;

    public Task<int> DeleteExpiredAsync(DateTimeOffset before, CancellationToken ct = default) =>
        db.UploadTickets.Where(t => t.ExpiresAt < before).ExecuteDeleteAsync(ct);

    private static async Task<int> ReleaseWhereAsync(IQueryable<UploadTicketEntity> receiving, int maxAttempts, CancellationToken ct, string? refusalCode = null)
    {
        var failed = await receiving.Where(t => t.Attempts >= maxAttempts)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.State, UploadTicketState.Failed)
                .SetProperty(t => t.ReceivingSince, (DateTimeOffset?)null)
                .SetProperty(t => t.LastRefusalCode, t => refusalCode ?? t.LastRefusalCode), ct);
        var reopened = await receiving.Where(t => t.Attempts < maxAttempts)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.State, UploadTicketState.Open)
                .SetProperty(t => t.ReceivingSince, (DateTimeOffset?)null)
                .SetProperty(t => t.LastRefusalCode, t => refusalCode ?? t.LastRefusalCode), ct);
        return failed + reopened;
    }

    private Task<UploadTicketEntity?> ReadAsync(Guid id, CancellationToken ct) =>
        db.UploadTickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);

    private static UploadClaimProbe Probe(UploadTicketEntity ticket, DateTimeOffset usableUntil, DateTimeOffset now) => ticket.State switch
    {
        UploadTicketState.Used => UploadClaimProbe.Used,
        UploadTicketState.Failed => UploadClaimProbe.Failed,
        _ when usableUntil <= now => UploadClaimProbe.Expired,
        UploadTicketState.Receiving => UploadClaimProbe.Receiving,
        _ => UploadClaimProbe.Open,
    };
}
