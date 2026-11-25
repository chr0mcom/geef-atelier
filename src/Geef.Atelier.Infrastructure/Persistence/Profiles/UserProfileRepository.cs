using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Core.Persistence.Profiles;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.Profiles;

internal sealed class UserProfileRepository(AtelierDbContext db) : IUserProfileRepository
{
    public Task<UserProfileEntity?> GetAsync(string username, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        return db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Username == username, ct);
    }

    public async Task UpsertAsync(UserProfileEntity profile, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Username);

        var formality = profile.Formality.ToString();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "UserProfiles" ("Username", "DisplayName", "Role", "Organization", "Formality", "Language", "Signature", "NoGos", "FreeText", "CreatedAt", "UpdatedAt")
            VALUES ({profile.Username}, {profile.DisplayName}, {profile.Role}, {profile.Organization}, {formality}, {profile.Language},
                    {profile.Signature}, {profile.NoGos}, {profile.FreeText}, {profile.CreatedAt}, {profile.UpdatedAt})
            ON CONFLICT ("Username") DO UPDATE SET
                "DisplayName" = EXCLUDED."DisplayName",
                "Role" = EXCLUDED."Role",
                "Organization" = EXCLUDED."Organization",
                "Formality" = EXCLUDED."Formality",
                "Language" = EXCLUDED."Language",
                "Signature" = EXCLUDED."Signature",
                "NoGos" = EXCLUDED."NoGos",
                "FreeText" = EXCLUDED."FreeText",
                "UpdatedAt" = EXCLUDED."UpdatedAt"
            """, ct);
    }

    public async Task<bool> DeleteAsync(string username, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        return await db.UserProfiles.Where(p => p.Username == username).ExecuteDeleteAsync(ct) > 0;
    }
}
