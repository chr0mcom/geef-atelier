using Geef.Atelier.Core.Domain.OAuth;
using Geef.Atelier.Core.Persistence.OAuth;

namespace Geef.Atelier.Tests.Fakes.OAuth;

public sealed class InMemoryOAuthAuditLogRepository : IOAuthAuditLogRepository
{
    public List<OAuthAuditLogEntry> Entries { get; } = [];

    public Task AddAsync(OAuthAuditLogEntry entry, CancellationToken ct)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}
