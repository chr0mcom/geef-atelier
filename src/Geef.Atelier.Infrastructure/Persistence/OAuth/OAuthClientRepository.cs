using Geef.Atelier.Core.Domain.OAuth;
using Geef.Atelier.Core.Persistence.OAuth;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.OAuth;

internal sealed class OAuthClientRepository(AtelierDbContext db) : IOAuthClientRepository
{
    public async Task<OAuthClient?> FindByClientIdAsync(string clientId, CancellationToken ct)
        => await db.OAuthClients.AsNoTracking().FirstOrDefaultAsync(c => c.ClientId == clientId, ct);

    public async Task AddAsync(OAuthClient client, CancellationToken ct)
    {
        db.OAuthClients.Add(client);
        await db.SaveChangesAsync(ct);
    }
}
