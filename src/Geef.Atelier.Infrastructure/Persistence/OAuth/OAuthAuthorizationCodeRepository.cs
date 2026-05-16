using Geef.Atelier.Core.Domain.OAuth;
using Geef.Atelier.Core.Persistence.OAuth;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.OAuth;

internal sealed class OAuthAuthorizationCodeRepository(AtelierDbContext db) : IOAuthAuthorizationCodeRepository
{
    public async Task AddAsync(OAuthAuthorizationCode code, CancellationToken ct)
    {
        db.OAuthAuthorizationCodes.Add(code);
        await db.SaveChangesAsync(ct);
    }

    public async Task<OAuthAuthorizationCode?> ConsumeAsync(string codeHash, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var count = await db.Database.ExecuteSqlAsync(
            $"""UPDATE "OAuthAuthorizationCodes" SET "UsedAt" = {now} WHERE "CodeHash" = {codeHash} AND "UsedAt" IS NULL""",
            ct);
        if (count == 0) return null;
        return await db.OAuthAuthorizationCodes
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CodeHash == codeHash, ct);
    }

    public async Task DeleteExpiredAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-1);
        await db.OAuthAuthorizationCodes
            .Where(c => c.ExpiresAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }
}
