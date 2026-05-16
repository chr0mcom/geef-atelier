using Geef.Atelier.Core.Domain.OAuth;

namespace Geef.Atelier.Core.Persistence.OAuth;

public interface IOAuthAuditLogRepository
{
    Task AddAsync(OAuthAuditLogEntry entry, CancellationToken ct);
    Task<IReadOnlyList<OAuthAuditLogEntry>> GetRecentByUserIdAsync(string userId, int limit, CancellationToken ct);
}
