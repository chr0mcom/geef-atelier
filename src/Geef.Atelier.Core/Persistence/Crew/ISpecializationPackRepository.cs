using Geef.Atelier.Core.Domain.Crew.Specialization;

namespace Geef.Atelier.Core.Persistence.Crew;

/// <summary>Persistence contract for <see cref="SpecializationPack"/> catalogue entries.</summary>
public interface ISpecializationPackRepository
{
    /// <summary>Returns the pack with the given <paramref name="name"/> (system or custom), or <c>null</c>.</summary>
    Task<SpecializationPack?> GetByNameAsync(string name, CancellationToken ct = default);

    /// <summary>Returns all packs (system + custom). Archived packs are included.</summary>
    Task<IReadOnlyList<SpecializationPack>> ListAsync(bool includeSystem = true, CancellationToken ct = default);

    /// <summary>
    /// Returns the packs that may be bound to an actor of <paramref name="actorType"/> in a crew of
    /// <paramref name="crewDomain"/> owned by <paramref name="owningCrewId"/>:
    /// General + DomainScoped matching the domain + own TaskBound. Foreign TaskBound packs and archived
    /// packs are excluded; results are also filtered by <c>ApplicableActorTypes</c>.
    /// </summary>
    Task<IReadOnlyList<SpecializationPack>> ListForBindingAsync(
        PackActorType actorType,
        string? crewDomain,
        string? owningCrewId,
        CancellationToken ct = default);

    /// <summary>Inserts or replaces a custom pack by name.</summary>
    Task UpsertAsync(SpecializationPack pack, CancellationToken ct = default);

    /// <summary>Renames a custom pack and returns nothing; throws when not found.</summary>
    Task RenameAsync(string oldName, string newName, CancellationToken ct = default);

    /// <summary>Removes the custom pack with the given <paramref name="name"/>. No-op when not found.</summary>
    Task DeleteAsync(string name, CancellationToken ct = default);

    /// <summary>Deletes all TaskBound custom packs owned by <paramref name="crewName"/> (cascade). Returns the count.</summary>
    Task<int> DeleteByOwningCrewAsync(string crewName, CancellationToken ct = default);

    /// <summary>Sets <c>LastUsedAt = now</c> for the given custom pack names (best-effort; ignores system/unknown).</summary>
    Task TouchLastUsedAsync(IReadOnlyCollection<string> names, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    /// Archives custom General/DomainScoped packs whose <c>LastUsedAt</c> is older than
    /// <paramref name="cutoff"/> (or null) and that are not referenced by <paramref name="referencedNames"/>.
    /// Returns the archived pack names. System packs are never archived.
    /// </summary>
    Task<IReadOnlyList<string>> ArchiveUnusedAsync(
        DateTimeOffset cutoff,
        IReadOnlyCollection<string> referencedNames,
        CancellationToken ct = default);
}
