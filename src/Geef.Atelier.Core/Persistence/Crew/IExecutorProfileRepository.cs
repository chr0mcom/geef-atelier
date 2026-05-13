using Geef.Atelier.Core.Domain.Crew.Profiles;

namespace Geef.Atelier.Core.Persistence.Crew;

/// <summary>
/// Unified read/write access to executor profiles. System profiles are served from code constants;
/// custom profiles are persisted in the database and auto-prefixed with <c>"custom-"</c>.
/// </summary>
public interface IExecutorProfileRepository
{
    /// <summary>Returns all executor profiles. When <paramref name="includeSystem"/> is false, only DB-backed custom profiles are returned.</summary>
    Task<IReadOnlyList<ExecutorProfile>> ListAsync(bool includeSystem = true, CancellationToken cancellationToken = default);

    /// <summary>Returns the executor profile with the given name, checking system constants first, then the database. Returns null if not found.</summary>
    Task<ExecutorProfile?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Persists a new custom executor profile. Throws if a profile with the same name already exists.</summary>
    Task CreateAsync(ExecutorProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Updates an existing custom executor profile identified by <see cref="ExecutorProfile.Name"/>.</summary>
    Task UpdateAsync(ExecutorProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Deletes the custom executor profile with the given name. Throws if the profile does not exist in the database.</summary>
    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}
