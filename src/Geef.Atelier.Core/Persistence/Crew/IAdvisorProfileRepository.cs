using Geef.Atelier.Core.Domain.Crew.Advisors;

namespace Geef.Atelier.Core.Persistence.Crew;

/// <summary>
/// Unified read/write access to advisor profiles. System profiles are served from code constants;
/// custom profiles are persisted in the database and auto-prefixed with <c>"custom-"</c>.
/// </summary>
public interface IAdvisorProfileRepository
{
    /// <summary>Returns all advisor profiles. When <paramref name="includeSystem"/> is false, only DB-backed custom profiles are returned.</summary>
    Task<IReadOnlyList<AdvisorProfile>> ListAsync(bool includeSystem = true, CancellationToken cancellationToken = default);

    /// <summary>Returns the advisor profile with the given name, checking system constants first, then the database. Returns null if not found.</summary>
    Task<AdvisorProfile?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Persists a new custom advisor profile. Throws if a profile with the same name already exists.</summary>
    Task CreateAsync(AdvisorProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Updates an existing custom advisor profile identified by <see cref="AdvisorProfile.Name"/>.</summary>
    Task UpdateAsync(AdvisorProfile profile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a custom advisor profile from <paramref name="oldName"/> to <paramref name="newName"/>,
    /// cascading the change into the <c>AdvisorProfileNames</c> list of every custom crew template
    /// that references it. Atomic. Throws if the profile does not exist in the database.
    /// </summary>
    Task RenameAsync(string oldName, string newName, CancellationToken cancellationToken = default);

    /// <summary>Deletes the custom advisor profile with the given name. Throws if the profile does not exist in the database.</summary>
    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}
