using Geef.Atelier.Core.Domain.OAuth;
using Geef.Atelier.Core.Persistence.OAuth;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.OAuth;

internal sealed class OAuthRefreshTokenRepository(AtelierDbContext db) : IOAuthRefreshTokenRepository
{
    public async Task AddAsync(OAuthRefreshToken token, CancellationToken ct)
    {
        db.OAuthRefreshTokens.Add(token);
        await db.SaveChangesAsync(ct);
    }

    public async Task<OAuthRefreshToken?> ConsumeAsync(string tokenHash, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var count = await db.Database.ExecuteSqlAsync(
            $"""UPDATE "OAuthRefreshTokens" SET "UsedAt" = {now} WHERE "TokenHash" = {tokenHash} AND "UsedAt" IS NULL AND "RevokedAt" IS NULL""",
            ct);
        if (count == 0) return null;
        return await db.OAuthRefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
    }

    public async Task<OAuthRefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct)
        => await db.OAuthRefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task RevokeByUserIdAsync(string userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await db.OAuthRefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    public async Task RevokeByClientIdAndUserIdAsync(string clientId, string userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await db.OAuthRefreshTokens
            .Where(t => t.ClientId == clientId && t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    public async Task DeleteExpiredAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-1);
        await db.OAuthRefreshTokens
            .Where(t => t.ExpiresAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }
}
