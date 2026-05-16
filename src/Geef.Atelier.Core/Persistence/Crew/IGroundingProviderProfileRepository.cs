using Geef.Atelier.Core.Domain.Crew.Grounding;

namespace Geef.Atelier.Core.Persistence.Crew;

/// <summary>Persistence access for custom <see cref="GroundingProviderProfile"/> records.</summary>
public interface IGroundingProviderProfileRepository
{
    /// <summary>Returns all custom profiles (system profiles are not persisted).</summary>
    Task<IReadOnlyList<GroundingProviderProfile>> ListAsync(CancellationToken ct);

    /// <summary>Returns the custom profile with the given name, or <c>null</c> if not found.</summary>
    Task<GroundingProviderProfile?> GetByNameAsync(string name, CancellationToken ct);

    /// <summary>Persists a new custom profile and returns it.</summary>
    Task<GroundingProviderProfile> CreateAsync(GroundingProviderProfile profile, CancellationToken ct);

    /// <summary>Replaces an existing custom profile. Throws if not found.</summary>
    Task<GroundingProviderProfile> UpdateAsync(GroundingProviderProfile profile, CancellationToken ct);

    /// <summary>
    /// Renames a custom profile from <paramref name="oldName"/> to <paramref name="newName"/>,
    /// cascading the change into the <c>GroundingProviderNames</c> list of every custom crew
    /// template that references it. Atomic. Throws if the profile does not exist.
    /// </summary>
    Task RenameAsync(string oldName, string newName, CancellationToken ct);

    /// <summary>Deletes a custom profile by name. Throws if not found or if the name belongs to a system profile.</summary>
    Task DeleteAsync(string name, CancellationToken ct);
}
