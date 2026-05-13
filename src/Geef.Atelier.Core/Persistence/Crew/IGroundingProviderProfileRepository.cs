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

    /// <summary>Deletes a custom profile by name. Throws if not found or if the name belongs to a system profile.</summary>
    Task DeleteAsync(string name, CancellationToken ct);
}
