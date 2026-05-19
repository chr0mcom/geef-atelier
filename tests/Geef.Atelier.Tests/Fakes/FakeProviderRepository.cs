using Geef.Atelier.Core.Domain.Providers;
using Geef.Atelier.Core.Persistence.Providers;

namespace Geef.Atelier.Tests.Fakes;

/// <summary>In-memory fake for <see cref="IProviderRepository"/> used in unit tests.</summary>
internal sealed class FakeProviderRepository : IProviderRepository
{
    private readonly List<Provider> _store = [];

    /// <summary>Names of providers that are considered referenced by a profile (for delete-guard tests).</summary>
    private readonly HashSet<string> _referencedNames = [];

    /// <summary>Marks a provider name as referenced so that <see cref="IsReferencedByAnyProfileAsync"/> returns true.</summary>
    public void AddReference(string name) => _referencedNames.Add(name);

    /// <inheritdoc/>
    public Task<IReadOnlyList<Provider>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var result = includeInactive
            ? _store.ToList()
            : _store.Where(p => p.IsActive).ToList();
        return Task.FromResult<IReadOnlyList<Provider>>(result);
    }

    /// <inheritdoc/>
    public Task<Provider?> GetByNameAsync(string name, CancellationToken ct)
        => Task.FromResult(_store.FirstOrDefault(p => p.Name == name));

    /// <inheritdoc/>
    public Task CreateAsync(Provider provider, CancellationToken ct)
    {
        _store.Add(provider);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task UpdateAsync(Provider provider, CancellationToken ct)
    {
        var idx = _store.FindIndex(p => p.Name == provider.Name);
        if (idx < 0)
            throw new InvalidOperationException($"Provider '{provider.Name}' not found.");
        _store[idx] = provider;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string name, CancellationToken ct)
    {
        var removed = _store.RemoveAll(p => p.Name == name);
        if (removed == 0)
            throw new InvalidOperationException($"Provider '{name}' not found.");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task SetActiveAsync(string name, bool isActive, CancellationToken ct)
    {
        var idx = _store.FindIndex(p => p.Name == name);
        if (idx < 0)
            throw new InvalidOperationException($"Provider '{name}' not found.");
        _store[idx] = _store[idx] with { IsActive = isActive };
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> IsReferencedByAnyProfileAsync(string name, CancellationToken ct)
        => Task.FromResult(_referencedNames.Contains(name));
}
