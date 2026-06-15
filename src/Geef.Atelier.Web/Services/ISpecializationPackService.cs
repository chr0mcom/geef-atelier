using Geef.Atelier.Application.Crew;
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

    /// <summary>Returns the names of crew templates that bind <paramref name="packName"/> to any actor.</summary>
    Task<IReadOnlyList<string>> FindReferencingTemplatesAsync(string packName, CancellationToken ct = default);

    /// <summary>
    /// Promotes a custom pack to a broader scope (TaskBound → DomainScoped/General). Runs a generality
    /// review first; only persists (clearing the owning crew) when the review approves. Returns the review.
    /// </summary>
    Task<GeneralityReviewResult> PromoteAsync(string name, PackScope targetScope, string? targetDomain, CancellationToken ct = default);

    /// <summary>Demotes a General pack to DomainScoped with the given domain. No generality review needed.</summary>
    Task DemoteAsync(string name, string targetDomain, CancellationToken ct = default);

    /// <summary>
    /// Clones a pack to a new custom pack at <paramref name="targetScope"/>, gated by a generality review.
    /// Returns the review; the clone is created only when the review approves.
    /// </summary>
    Task<GeneralityReviewResult> CloneToGeneralizeAsync(
        string sourceName, string newName, PackScope targetScope, string? targetDomain, CancellationToken ct = default);
}
