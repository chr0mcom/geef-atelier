using Geef.Atelier.Core.Domain.Profiles;

namespace Geef.Atelier.Core.Persistence.Profiles;

/// <summary>Persistence of user profiles, one row per user name.</summary>
public interface IUserProfileRepository
{
    /// <summary>Returns the profile of <paramref name="username"/>, or <c>null</c> when there is none.</summary>
    Task<UserProfileEntity?> GetAsync(string username, CancellationToken ct = default);

    /// <summary>Inserts or replaces the profile of <see cref="UserProfileEntity.Username"/>.</summary>
    Task UpsertAsync(UserProfileEntity profile, CancellationToken ct = default);

    /// <summary>
    /// Puts the values of <paramref name="fill"/> into the fields of the profile of <paramref name="username"/> that are empty, in one
    /// locked step, and creates the profile when there is none; a field that holds a value keeps it, and every other field stays as it is,
    /// also when other saves run at the same time. <see cref="UserProfileEntity.UpdatedAt"/> becomes <paramref name="now"/> only when a
    /// field was filled. When the profile is deleted between creating and filling it, the row is created again (a few times at most);
    /// if that never holds, nothing is filled and nothing reported as already set.
    /// </summary>
    Task<UserProfileFillResult> FillEmptyAsync(string username, UserProfileFill fill, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Deletes the profile; false when there was none.</summary>
    Task<bool> DeleteAsync(string username, CancellationToken ct = default);
}
