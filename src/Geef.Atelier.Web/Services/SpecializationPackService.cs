using Geef.Atelier.Application.Crew;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Specialization;
using Geef.Atelier.Core.Persistence.Crew;

namespace Geef.Atelier.Web.Services;

/// <summary>
/// Application-layer façade over <see cref="ISpecializationPackRepository"/>, adding guard-rails for
/// system-pack immutability, the <c>custom-</c> name prefix, scope-consistency validation, and the
/// promote/demote/clone-to-generalize lifecycle (gated by a generality review).
/// </summary>
internal sealed class SpecializationPackService(
    ISpecializationPackRepository repository,
    ICrewTemplateRepository templateRepository,
    IPackGeneralityReviewer generalityReviewer) : ISpecializationPackService
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

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> FindReferencingTemplatesAsync(string packName, CancellationToken ct = default)
    {
        var templates = await templateRepository.ListAsync(includeSystem: true, ct);
        return templates
            .Where(t => t.ActorPackBindings.Values.Any(list => list.Contains(packName)))
            .Select(t => t.Name)
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<GeneralityReviewResult> PromoteAsync(
        string name, PackScope targetScope, string? targetDomain, CancellationToken ct = default)
    {
        if (targetScope == PackScope.TaskBound)
            throw new InvalidOperationException("Promote must target a broader scope (General or DomainScoped).");

        var pack = await repository.GetByNameAsync(name, ct)
            ?? throw new InvalidOperationException($"Pack '{name}' not found.");
        if (pack.IsSystem)
            throw new InvalidOperationException($"System pack '{name}' cannot be promoted.");
        if (targetScope == PackScope.DomainScoped && string.IsNullOrWhiteSpace(targetDomain))
            throw new InvalidOperationException("Promotion to DomainScoped requires a domain.");

        var review = await generalityReviewer.ReviewAsync(pack, targetScope, ct);
        if (!review.Approved)
            return review;

        var promoted = pack with
        {
            Scope = targetScope,
            Domain = targetScope == PackScope.DomainScoped ? targetDomain : null,
            OwningCrewId = null,
            Archived = false,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await repository.UpsertAsync(promoted, ct);
        return review;
    }

    /// <inheritdoc/>
    public async Task DemoteAsync(string name, string targetDomain, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(targetDomain))
            throw new InvalidOperationException("Demotion to DomainScoped requires a domain.");

        var pack = await repository.GetByNameAsync(name, ct)
            ?? throw new InvalidOperationException($"Pack '{name}' not found.");
        if (pack.IsSystem)
            throw new InvalidOperationException($"System pack '{name}' cannot be demoted.");

        var demoted = pack with
        {
            Scope = PackScope.DomainScoped,
            Domain = targetDomain,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await repository.UpsertAsync(demoted, ct);
    }

    /// <inheritdoc/>
    public async Task<GeneralityReviewResult> CloneToGeneralizeAsync(
        string sourceName, string newName, PackScope targetScope, string? targetDomain, CancellationToken ct = default)
    {
        var source = await repository.GetByNameAsync(sourceName, ct)
            ?? throw new InvalidOperationException($"Pack '{sourceName}' not found.");
        if (targetScope == PackScope.DomainScoped && string.IsNullOrWhiteSpace(targetDomain))
            throw new InvalidOperationException("Cloning to DomainScoped requires a domain.");

        var review = await generalityReviewer.ReviewAsync(source, targetScope, ct);
        if (!review.Approved)
            return review;

        var now = DateTimeOffset.UtcNow;
        var clone = source with
        {
            Name = SystemCrew.EnsureCustomPrefix(newName),
            DisplayName = source.DisplayName + " (generalized)",
            Scope = targetScope,
            Domain = targetScope == PackScope.DomainScoped ? targetDomain : null,
            OwningCrewId = null,
            IsSystem = false,
            Archived = false,
            CreatedAt = now,
            UpdatedAt = now,
            LastUsedAt = null
        };
        Validate(clone);
        await repository.UpsertAsync(clone, ct);
        return review;
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
