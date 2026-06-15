using Geef.Atelier.Core.Domain.Crew.Specialization;

namespace Geef.Atelier.Web.Services;

/// <summary>Application-level service for managing <see cref="SpecializationPack"/> catalogue entries.</summary>
public interface ISpecializationPackService
{
    /// <summary>Returns all packs (system + custom).</summary>
    Task<IReadOnlyList<SpecializationPack>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Returns the pack with the given <paramref name="name"/>, or <c>null</c> when not found.</summary>
    Task<SpecializationPack?> GetByNameAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Returns packs bindable to an actor of <paramref name="actorType"/> in a crew of
    /// <paramref name="crewDomain"/> owned by <paramref name="owningCrewId"/> (scope/type filtered;
    /// foreign TaskBound and archived excluded).
    /// </summary>
    Task<IReadOnlyList<SpecializationPack>> ListForBindingAsync(
        PackActorType actorType, string? crewDomain, string? owningCrewId, CancellationToken ct = default);

    /// <summary>
    /// Inserts or replaces a custom pack. Throws when attempting to overwrite a system pack or when the
    /// pack is invalid (DomainScoped without domain, TaskBound without owning crew).
    /// </summary>
    Task SaveAsync(SpecializationPack pack, CancellationToken ct = default);

    /// <summary>Removes the custom pack with the given <paramref name="name"/>. Throws for system packs.</summary>
    Task DeleteAsync(string name, CancellationToken ct = default);
}
