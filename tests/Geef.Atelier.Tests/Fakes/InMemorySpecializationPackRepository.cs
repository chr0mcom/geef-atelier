using Geef.Atelier.Core.Domain.Crew.Specialization;
using Geef.Atelier.Core.Persistence.Crew;

namespace Geef.Atelier.Tests.Fakes;

/// <summary>In-memory <see cref="ISpecializationPackRepository"/> for tests (system packs + custom map).</summary>
public sealed class InMemorySpecializationPackRepository : ISpecializationPackRepository
{
    private readonly Dictionary<string, SpecializationPack> _custom = new(StringComparer.Ordinal);

    public Task<SpecializationPack?> GetByNameAsync(string name, CancellationToken ct = default) =>
        Task.FromResult(SystemPacks.ByName.TryGetValue(name, out var sys)
            ? sys
            : _custom.GetValueOrDefault(name));

    public Task<IReadOnlyList<SpecializationPack>> ListAsync(bool includeSystem = true, CancellationToken ct = default)
    {
        IEnumerable<SpecializationPack> all = _custom.Values;
        if (includeSystem) all = SystemPacks.All.Concat(all);
        return Task.FromResult<IReadOnlyList<SpecializationPack>>(all.ToList());
    }

    public async Task<IReadOnlyList<SpecializationPack>> ListForBindingAsync(
        PackActorType actorType, string? crewDomain, string? owningCrewId, CancellationToken ct = default)
    {
        var all = await ListAsync(true, ct);
        return all.Where(p => !p.Archived
                              && p.ApplicableActorTypes.AppliesTo(actorType)
                              && InScope(p, crewDomain, owningCrewId)).ToList();
    }

    private static bool InScope(SpecializationPack p, string? domain, string? owningCrewId) => p.Scope switch
    {
        PackScope.General      => true,
        PackScope.DomainScoped => !string.IsNullOrWhiteSpace(domain)
                                  && string.Equals(p.Domain, domain, StringComparison.OrdinalIgnoreCase),
        PackScope.TaskBound    => !string.IsNullOrWhiteSpace(owningCrewId)
                                  && string.Equals(p.OwningCrewId, owningCrewId, StringComparison.Ordinal),
        _                      => false
    };

    public Task UpsertAsync(SpecializationPack pack, CancellationToken ct = default)
    {
        _custom[pack.Name] = pack;
        return Task.CompletedTask;
    }

    public Task RenameAsync(string oldName, string newName, CancellationToken ct = default)
    {
        if (_custom.Remove(oldName, out var p))
            _custom[newName] = p with { Name = newName };
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string name, CancellationToken ct = default)
    {
        _custom.Remove(name);
        return Task.CompletedTask;
    }

    public Task<int> DeleteByOwningCrewAsync(string crewName, CancellationToken ct = default)
    {
        var removed = _custom.Values
            .Where(p => p.Scope == PackScope.TaskBound && p.OwningCrewId == crewName)
            .Select(p => p.Name).ToList();
        foreach (var n in removed) _custom.Remove(n);
        return Task.FromResult(removed.Count);
    }

    public Task TouchLastUsedAsync(IReadOnlyCollection<string> names, DateTimeOffset now, CancellationToken ct = default)
    {
        foreach (var n in names)
            if (_custom.TryGetValue(n, out var p))
                _custom[n] = p with { LastUsedAt = now };
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ArchiveUnusedAsync(
        DateTimeOffset cutoff, IReadOnlyCollection<string> referencedNames, CancellationToken ct = default)
    {
        var archived = new List<string>();
        foreach (var p in _custom.Values.ToList())
        {
            if (p.IsSystem || p.Archived) continue;
            if (p.Scope is not (PackScope.General or PackScope.DomainScoped)) continue;
            if (referencedNames.Contains(p.Name)) continue;
            if (p.LastUsedAt is not null && p.LastUsedAt >= cutoff) continue;
            _custom[p.Name] = p with { Archived = true };
            archived.Add(p.Name);
        }
        return Task.FromResult<IReadOnlyList<string>>(archived);
    }
}
