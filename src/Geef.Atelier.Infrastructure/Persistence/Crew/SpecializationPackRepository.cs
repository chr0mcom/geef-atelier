using Geef.Atelier.Core.Domain.Crew.Specialization;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.Crew;

internal sealed class SpecializationPackRepository(AtelierDbContext db) : ISpecializationPackRepository
{
    /// <inheritdoc/>
    public async Task<SpecializationPack?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        if (SystemPacks.ByName.TryGetValue(name, out var system))
            return system;
        var entity = await db.SpecializationPacks.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Name == name, ct);
        return entity?.ToDomain();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SpecializationPack>> ListAsync(bool includeSystem = true, CancellationToken ct = default)
    {
        var custom = await db.SpecializationPacks.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);
        var customDomain = custom.Select(e => e.ToDomain());
        return includeSystem
            ? SystemPacks.All.Concat(customDomain).ToList()
            : customDomain.ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SpecializationPack>> ListForBindingAsync(
        PackActorType actorType,
        string? crewDomain,
        string? owningCrewId,
        CancellationToken ct = default)
    {
        var all = await ListAsync(includeSystem: true, ct);
        return all.Where(p => !p.Archived
                              && p.ApplicableActorTypes.AppliesTo(actorType)
                              && IsInScope(p, crewDomain, owningCrewId))
                  .ToList();
    }

    private static bool IsInScope(SpecializationPack p, string? crewDomain, string? owningCrewId) => p.Scope switch
    {
        PackScope.General      => true,
        PackScope.DomainScoped => !string.IsNullOrWhiteSpace(crewDomain)
                                  && string.Equals(p.Domain, crewDomain, StringComparison.OrdinalIgnoreCase),
        PackScope.TaskBound    => !string.IsNullOrWhiteSpace(owningCrewId)
                                  && string.Equals(p.OwningCrewId, owningCrewId, StringComparison.Ordinal),
        _                      => false
    };

    /// <inheritdoc/>
    public async Task UpsertAsync(SpecializationPack pack, CancellationToken ct = default)
    {
        var existing = await db.SpecializationPacks.FirstOrDefaultAsync(p => p.Name == pack.Name, ct);
        if (existing is null)
        {
            db.SpecializationPacks.Add(SpecializationPackEntity.FromDomain(pack));
        }
        else
        {
            var updated = SpecializationPackEntity.FromDomain(pack);
            existing.DisplayName = updated.DisplayName;
            existing.Description = updated.Description;
            existing.SpecializationText = updated.SpecializationText;
            existing.Scope = updated.Scope;
            existing.Domain = updated.Domain;
            existing.ApplicableActorTypes = updated.ApplicableActorTypes;
            existing.OwningCrewId = updated.OwningCrewId;
            existing.IsSystem = updated.IsSystem;
            existing.Archived = updated.Archived;
            existing.UpdatedAt = updated.UpdatedAt;
            existing.LastUsedAt = updated.LastUsedAt;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task RenameAsync(string oldName, string newName, CancellationToken ct = default)
    {
        var affected = await db.SpecializationPacks
            .Where(p => p.Name == oldName)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, newName), ct);
        if (affected == 0)
            throw new InvalidOperationException($"Specialization pack '{oldName}' not found in the database.");
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string name, CancellationToken ct = default)
    {
        await db.SpecializationPacks.Where(p => p.Name == name).ExecuteDeleteAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<int> DeleteByOwningCrewAsync(string crewName, CancellationToken ct = default)
    {
        return await db.SpecializationPacks
            .Where(p => p.OwningCrewId == crewName && p.Scope == (int)PackScope.TaskBound)
            .ExecuteDeleteAsync(ct);
    }

    /// <inheritdoc/>
    public async Task TouchLastUsedAsync(IReadOnlyCollection<string> names, DateTimeOffset now, CancellationToken ct = default)
    {
        if (names.Count == 0) return;
        await db.SpecializationPacks
            .Where(p => names.Contains(p.Name))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastUsedAt, now), ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> ArchiveUnusedAsync(
        DateTimeOffset cutoff,
        IReadOnlyCollection<string> referencedNames,
        CancellationToken ct = default)
    {
        var candidates = await db.SpecializationPacks
            .Where(p => !p.Archived
                        && !p.IsSystem
                        && (p.Scope == (int)PackScope.General || p.Scope == (int)PackScope.DomainScoped)
                        && (p.LastUsedAt == null || p.LastUsedAt < cutoff)
                        && !referencedNames.Contains(p.Name))
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return [];

        foreach (var c in candidates)
            c.Archived = true;
        await db.SaveChangesAsync(ct);

        return candidates.Select(c => c.Name).ToList();
    }
}
