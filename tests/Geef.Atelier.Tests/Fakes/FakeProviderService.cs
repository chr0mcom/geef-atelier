using Geef.Atelier.Application.Providers;
using Geef.Atelier.Core.Domain.Providers;

namespace Geef.Atelier.Tests.Fakes;

/// <summary>
/// In-memory fake for <see cref="IProviderService"/>.
/// System providers are always merged in from <see cref="SystemProviders"/>.
/// <see cref="TestConnectionAsync"/> returns a synthetic success result without network I/O.
/// </summary>
internal sealed class FakeProviderService : IProviderService
{
    private readonly FakeProviderRepository _repo = new();

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Provider>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var system = SystemProviders.ProvidersByName.Values.ToList();
        var custom = await _repo.ListAsync(includeInactive: includeInactive, ct);
        return [.. system, .. custom];
    }

    /// <inheritdoc/>
    public async Task<Provider?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        if (SystemProviders.ProvidersByName.TryGetValue(name, out var system))
            return system;
        return await _repo.GetByNameAsync(name, ct);
    }

    /// <inheritdoc/>
    public async Task<Provider> CreateCustomAsync(Provider provider, CancellationToken ct = default)
    {
        if (SystemProviders.IsSystemProviderName(provider.Name))
            throw new InvalidOperationException("System provider is read-only.");

        var name = SystemProviders.EnsureCustomPrefix(provider.Name);
        var now = DateTimeOffset.UtcNow;
        var normalized = provider with { Name = name, IsSystem = false, CreatedAt = now, UpdatedAt = now };
        await _repo.CreateAsync(normalized, ct);
        return normalized;
    }

    /// <inheritdoc/>
    public async Task<Provider> UpdateCustomAsync(string name, Provider provider, CancellationToken ct = default)
    {
        if (SystemProviders.IsSystemProviderName(name))
            throw new InvalidOperationException("System provider is read-only.");

        var existing = await _repo.GetByNameAsync(name, ct)
            ?? throw new InvalidOperationException($"Provider '{name}' not found.");

        var updated = provider with
        {
            Name = existing.Name,
            IsSystem = false,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await _repo.UpdateAsync(updated, ct);
        return updated;
    }

    /// <inheritdoc/>
    public async Task DeleteCustomAsync(string name, CancellationToken ct = default)
    {
        if (SystemProviders.IsSystemProviderName(name))
            throw new InvalidOperationException("System providers cannot be deleted.");

        if (await _repo.IsReferencedByAnyProfileAsync(name, ct))
            throw new InvalidOperationException("Provider is referenced by profiles and cannot be deleted.");

        await _repo.DeleteAsync(name, ct);
    }

    /// <inheritdoc/>
    public async Task SetActiveAsync(string name, bool isActive, CancellationToken ct = default)
    {
        if (SystemProviders.IsSystemProviderName(name))
            return; // System providers are always considered active — silently skip.
        await _repo.SetActiveAsync(name, isActive, ct);
    }

    /// <inheritdoc/>
    public Task<ConnectionTestResult> TestConnectionAsync(string name, CancellationToken ct = default)
        => Task.FromResult(new ConnectionTestResult(
            Success: true,
            LatencyMs: 42,
            ErrorMessage: null,
            ResponseSample: null));
}
