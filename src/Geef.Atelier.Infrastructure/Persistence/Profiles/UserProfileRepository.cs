using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Core.Persistence.Profiles;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.Profiles;

internal sealed class UserProfileRepository(AtelierDbContext db) : IUserProfileRepository
{
    /// <summary>How often <see cref="FillEmptyAsync"/> creates the row again when the profile was deleted between its two statements.</summary>
    internal const int FillAttempts = 3;

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

    public async Task<UserProfileFillResult> FillEmptyAsync(string username, UserProfileFill fill, DateTimeOffset now, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(fill);

        var name = fill.DisplayName;
        var formality = fill.Formality is { } chosen && chosen != Formality.Unset ? chosen.ToString() : null;
        var language = fill.Language;
        var signature = fill.Signature;
        var unset = nameof(Formality.Unset);
        FilledRow? old = null;
        for (var attempt = 0; attempt < FillAttempts && old is null; attempt++)
        {
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "UserProfiles" ("Username", "Formality", "CreatedAt", "UpdatedAt")
                VALUES ({username}, {unset}, {now}, {now})
                ON CONFLICT ("Username") DO NOTHING
                """, ct);
            var before = await db.Database.SqlQuery<FilledRow>($"""
                UPDATE "UserProfiles" AS p SET
                    "DisplayName" = COALESCE(o."DisplayName", {name}::text),
                    "Formality" = CASE WHEN o."Formality" = {unset} AND {formality}::text IS NOT NULL THEN {formality}::text ELSE o."Formality" END,
                    "Language" = COALESCE(o."Language", {language}::text),
                    "Signature" = COALESCE(o."Signature", {signature}::text),
                    "UpdatedAt" = CASE
                        WHEN (o."DisplayName" IS NULL AND {name}::text IS NOT NULL)
                          OR (o."Formality" = {unset} AND {formality}::text IS NOT NULL)
                          OR (o."Language" IS NULL AND {language}::text IS NOT NULL)
                          OR (o."Signature" IS NULL AND {signature}::text IS NOT NULL)
                        THEN {now} ELSE o."UpdatedAt" END
                FROM (SELECT "Username", "DisplayName", "Formality", "Language", "Signature", "UpdatedAt"
                      FROM "UserProfiles" WHERE "Username" = {username} FOR UPDATE) AS o
                WHERE p."Username" = o."Username"
                RETURNING o."DisplayName" AS "DisplayName", o."Formality" AS "Formality", o."Language" AS "Language", o."Signature" AS "Signature"
                """).ToListAsync(ct);
            old = before.SingleOrDefault();
        }
        if (old is null)
            return new UserProfileFillResult([], []);

        var filled = new List<ProfileFillField>();
        var alreadySet = new List<ProfileFillField>();
        void Sort(ProfileFillField field, bool asked, bool empty)
        {
            if (asked)
                (empty ? filled : alreadySet).Add(field);
        }
        Sort(ProfileFillField.DisplayName, name is not null, old.DisplayName is null);
        Sort(ProfileFillField.Formality, formality is not null, old.Formality == unset);
        Sort(ProfileFillField.Language, language is not null, old.Language is null);
        Sort(ProfileFillField.Signature, signature is not null, old.Signature is null);
        return new UserProfileFillResult(filled, alreadySet);
    }

    public async Task<bool> DeleteAsync(string username, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        return await db.UserProfiles.Where(p => p.Username == username).ExecuteDeleteAsync(ct) > 0;
    }
}

/// <summary>The fields of a profile row before <see cref="UserProfileRepository.FillEmptyAsync"/> filled it.</summary>
internal sealed class FilledRow
{
    public string? DisplayName { get; init; }
    public string Formality { get; init; } = string.Empty;
    public string? Language { get; init; }
    public string? Signature { get; init; }
}
