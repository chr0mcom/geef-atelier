using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Persistence.Crew;

namespace Geef.Atelier.Tests.Fakes;

/// <summary>In-memory fake for test scenarios that need a non-null finalizer repository.</summary>
internal sealed class InMemoryFinalizerProfileRepository : IFinalizerProfileRepository
{
    private readonly List<FinalizerProfile> _store = [];

    public Task<IReadOnlyList<FinalizerProfile>> ListAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<FinalizerProfile>>(_store.ToList());

    public Task<FinalizerProfile?> GetByNameAsync(string name, CancellationToken ct)
        => Task.FromResult(_store.FirstOrDefault(p => p.Name == name));

    public Task<FinalizerProfile> CreateAsync(FinalizerProfile profile, CancellationToken ct)
    {
        _store.Add(profile);
        return Task.FromResult(profile);
    }

    public Task<FinalizerProfile> UpdateAsync(FinalizerProfile profile, CancellationToken ct)
    {
        var idx = _store.FindIndex(p => p.Name == profile.Name);
        if (idx >= 0) _store[idx] = profile;
        return Task.FromResult(profile);
    }

    public Task RenameAsync(string oldName, string newName, CancellationToken ct)
    {
        var idx = _store.FindIndex(p => p.Name == oldName);
        if (idx >= 0) _store[idx] = _store[idx] with { Name = newName };
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string name, CancellationToken ct)
    {
        _store.RemoveAll(p => p.Name == name);
        return Task.CompletedTask;
    }
}
