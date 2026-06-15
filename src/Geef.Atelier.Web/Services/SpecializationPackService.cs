using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Specialization;
using Geef.Atelier.Core.Persistence.Crew;

namespace Geef.Atelier.Web.Services;

/// <summary>
/// Application-layer façade over <see cref="ISpecializationPackRepository"/>, adding guard-rails for
/// system-pack immutability, the <c>custom-</c> name prefix, and scope-consistency validation.
/// </summary>
internal sealed class SpecializationPackService(ISpecializationPackRepository repository) : ISpecializationPackService
{
    /// <inheritdoc/>
    public Task<IReadOnlyList<SpecializationPack>> GetAllAsync(CancellationToken ct = default) =>
        repository.ListAsync(includeSystem: true, ct);

    /// <inheritdoc/>
    public Task<SpecializationPack?> GetByNameAsync(string name, CancellationToken ct = default) =>
        repository.GetByNameAsync(name, ct);

    /// <inheritdoc/>
    public Task<IReadOnlyList<SpecializationPack>> ListForBindingAsync(
        PackActorType actorType, string? crewDomain, string? owningCrewId, CancellationToken ct = default) =>
        repository.ListForBindingAsync(actorType, crewDomain, owningCrewId, ct);

    /// <inheritdoc/>
    public async Task SaveAsync(SpecializationPack pack, CancellationToken ct = default)
    {
        var existing = await repository.GetByNameAsync(pack.Name, ct);
        if (existing?.IsSystem == true)
            throw new InvalidOperationException($"System pack '{pack.Name}' cannot be modified.");

        Validate(pack);
        await repository.UpsertAsync(pack, ct);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string name, CancellationToken ct = default)
    {
        var existing = await repository.GetByNameAsync(name, ct);
        if (existing?.IsSystem == true)
            throw new InvalidOperationException($"System pack '{name}' cannot be deleted.");

        await repository.DeleteAsync(name, ct);
    }

    private static void Validate(SpecializationPack pack)
    {
        if (!SpecializationPack.IsValidName(pack.Name))
            throw new InvalidOperationException(
                $"Pack name '{pack.Name}' is invalid (lowercase letters, digits and hyphens only).");

        if (pack.Scope == PackScope.DomainScoped && string.IsNullOrWhiteSpace(pack.Domain))
            throw new InvalidOperationException("DomainScoped packs require a domain.");

        if (pack.Scope == PackScope.TaskBound && string.IsNullOrWhiteSpace(pack.OwningCrewId))
            throw new InvalidOperationException("TaskBound packs require an owning crew.");
    }

    /// <summary>Ensures the <c>custom-</c> prefix on a user-authored pack name.</summary>
    public static string EnsureCustomPrefix(string name) => SystemCrew.EnsureCustomPrefix(name);
}
