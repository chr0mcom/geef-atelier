using Geef.Atelier.Core.Domain.Profiles;

namespace Geef.Atelier.Core.Persistence.Profiles;

/// <summary>Persistence of user profiles, one row per user name.</summary>
public interface IUserProfileRepository
{
    /// <summary>Returns the profile of <paramref name="username"/>, or <c>null</c> when there is none.</summary>
    Task<UserProfileEntity?> GetAsync(string username, CancellationToken ct = default);

    /// <summary>Inserts or replaces the profile of <see cref="UserProfileEntity.Username"/>.</summary>
    Task UpsertAsync(UserProfileEntity profile, CancellationToken ct = default);

    /// <summary>Deletes the profile; false when there was none.</summary>
    Task<bool> DeleteAsync(string username, CancellationToken ct = default);
}
