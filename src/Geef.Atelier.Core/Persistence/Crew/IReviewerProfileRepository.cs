using Geef.Atelier.Core.Domain.Crew.Profiles;

namespace Geef.Atelier.Core.Persistence.Crew;

/// <summary>
/// Unified read/write access to reviewer profiles. System profiles are served from code constants;
/// custom profiles are persisted in the database and auto-prefixed with <c>"custom-"</c>.
/// </summary>
public interface IReviewerProfileRepository
{
    /// <summary>Returns all reviewer profiles. When <paramref name="includeSystem"/> is false, only DB-backed custom profiles are returned.</summary>
    Task<IReadOnlyList<ReviewerProfile>> ListAsync(bool includeSystem = true, CancellationToken cancellationToken = default);

    /// <summary>Returns the reviewer profile with the given name, checking system constants first, then the database. Returns null if not found.</summary>
    Task<ReviewerProfile?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Persists a new custom reviewer profile. Throws if a profile with the same name already exists.</summary>
    Task CreateAsync(ReviewerProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Updates an existing custom reviewer profile identified by <see cref="ReviewerProfile.Name"/>.</summary>
    Task UpdateAsync(ReviewerProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Deletes the custom reviewer profile with the given name. Throws if the profile does not exist in the database.</summary>
    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}
