using Geef.Atelier.Core.Domain.OAuth;
using Geef.Atelier.Core.Persistence.OAuth;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.OAuth;

internal sealed class OAuthAccessTokenRepository(AtelierDbContext db) : IOAuthAccessTokenRepository
{
    public async Task AddAsync(OAuthAccessToken token, CancellationToken ct)
    {
        db.OAuthAccessTokens.Add(token);
        await db.SaveChangesAsync(ct);
    }

    public async Task<OAuthAccessToken?> FindByHashAsync(string tokenHash, CancellationToken ct)
        => await db.OAuthAccessTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task RevokeByHashAsync(string tokenHash, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await db.OAuthAccessTokens
            .Where(t => t.TokenHash == tokenHash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    public async Task RevokeByUserIdAsync(string userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await db.OAuthAccessTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    public async Task RevokeByClientIdAndUserIdAsync(string clientId, string userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await db.OAuthAccessTokens
            .Where(t => t.ClientId == clientId && t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    public async Task DeleteExpiredAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-1);
        await db.OAuthAccessTokens
            .Where(t => t.ExpiresAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    public async Task<IReadOnlyList<OAuthAccessToken>> GetActiveTokensByUserIdAsync(string userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await db.OAuthAccessTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
            .ToListAsync(ct);
    }
}
