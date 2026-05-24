using Geef.Atelier.Core.Domain.OAuth;
using Geef.Atelier.Core.Persistence.OAuth;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.OAuth;

internal sealed class OAuthClientRepository(AtelierDbContext db) : IOAuthClientRepository
{
    public async Task<OAuthClient?> FindByClientIdAsync(string clientId, CancellationToken ct)
        => await db.OAuthClients.AsNoTracking().FirstOrDefaultAsync(c => c.ClientId == clientId, ct);

    public async Task<IReadOnlyList<OAuthClient>> GetAllAsync(CancellationToken ct)
        => await db.OAuthClients.AsNoTracking().OrderBy(c => c.ClientName).ToListAsync(ct);

    public async Task AddAsync(OAuthClient client, CancellationToken ct)
    {
        db.OAuthClients.Add(client);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            db.Entry(client).State = EntityState.Detached;
            throw;
        }
    }

    public async Task DeleteAsync(string clientId, CancellationToken ct)
    {
        await db.OAuthClients
            .Where(c => c.ClientId == clientId)
            .ExecuteDeleteAsync(ct);
    }
}
