using Geef.Atelier.Core.Domain.OAuth;
using Geef.Atelier.Core.Persistence.OAuth;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.OAuth;

internal sealed class OAuthAuditLogRepository(AtelierDbContext db) : IOAuthAuditLogRepository
{
    public async Task AddAsync(OAuthAuditLogEntry entry, CancellationToken ct)
    {
        db.OAuthAuditLog.Add(entry);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<OAuthAuditLogEntry>> GetRecentByUserIdAsync(string userId, int limit, CancellationToken ct)
        => await db.OAuthAuditLog
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
}
