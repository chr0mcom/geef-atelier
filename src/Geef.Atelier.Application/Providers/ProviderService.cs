namespace Geef.Atelier.Application.Providers;

using Geef.Atelier.Core.Domain.Providers;
using Geef.Atelier.Application.Security;
using Geef.Atelier.Core.Persistence.Providers;

internal sealed class ProviderService(
    IProviderRepository repository,
    IEnvReferencePolicy? envReferencePolicy = null) : IProviderService
{
    private readonly IEnvReferencePolicy _envPolicy = envReferencePolicy ?? EnvReferencePolicy.Default;

    private const string SystemReadOnlyMessage = "System provider is read-only.";
    private const string SystemCannotBeDeletedMessage = "System providers cannot be deleted.";

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Provider>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        // Always load all DB providers so we can read active-state overrides for system providers.
        var allDbProviders = await repository.ListAsync(includeInactive: true, ct);

        // DB rows for system providers carry the persisted active-state (may differ from the code default of true).
        var systemActiveStates = allDbProviders
            .Where(p => p.IsSystem)
            .ToDictionary(p => p.Name, p => p.IsActive);

        // Merge: code-constant system providers with DB active-state override when present.
        var systemProviders = SystemProviders.ProvidersByName.Values
            .Select(p => systemActiveStates.TryGetValue(p.Name, out var active) ? p with { IsActive = active } : p);

        var customProviders = allDbProviders.Where(p => !p.IsSystem);

        var all = systemProviders.Concat(customProviders);
        if (!includeInactive)
            all = all.Where(p => p.IsActive);

        return [.. all];
    }

    /// <inheritdoc/>
    public async Task<Provider?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        if (SystemProviders.ProvidersByName.TryGetValue(name, out var system))
            return system;

        return await repository.GetByNameAsync(name, ct);
    }

    /// <inheritdoc/>
    public async Task<Provider> CreateCustomAsync(Provider provider, CancellationToken ct = default)
    {
        if (SystemProviders.IsSystemProviderName(provider.Name))
            throw new InvalidOperationException(SystemReadOnlyMessage);

        EnsureEnvReferencesAllowed(provider);
        var name = SystemProviders.EnsureCustomPrefix(provider.Name);
        var now = DateTimeOffset.UtcNow;
        var normalized = provider with { Name = name, IsSystem = false, CreatedAt = now, UpdatedAt = now };
        await repository.CreateAsync(normalized, ct);
        return normalized;
    }

    /// <inheritdoc/>
    public async Task<Provider> UpdateCustomAsync(string name, Provider provider, CancellationToken ct = default)
    {
        if (SystemProviders.IsSystemProviderName(name))
            throw new InvalidOperationException(SystemReadOnlyMessage);

        EnsureEnvReferencesAllowed(provider);
        var existing = await repository.GetByNameAsync(name, ct)
            ?? throw new InvalidOperationException($"Provider '{name}' not found.");

        var updated = provider with { Name = existing.Name, IsSystem = false, CreatedAt = existing.CreatedAt, UpdatedAt = DateTimeOffset.UtcNow };
        await repository.UpdateAsync(updated, ct);
        return updated;
    }

    /// <inheritdoc/>
    public async Task DeleteCustomAsync(string name, CancellationToken ct = default)
    {
        if (SystemProviders.IsSystemProviderName(name))
            throw new InvalidOperationException(SystemCannotBeDeletedMessage);

        await repository.DeleteAsync(name, ct);
    }

    /// <inheritdoc/>
    public async Task SetActiveAsync(string name, bool isActive, CancellationToken ct = default)
    {
        if (SystemProviders.IsSystemProviderName(name))
        {
            // Persist the active-state override for this system provider.
            // If the provider was added to SystemProviders after the seeding migration, insert its DB row now.
            var existing = await repository.GetByNameAsync(name, ct);
            if (existing is null)
                await repository.CreateAsync(SystemProviders.ProvidersByName[name] with { IsActive = isActive }, ct);
            else
                await repository.SetActiveAsync(name, isActive, ct);
            return;
        }

        await repository.SetActiveAsync(name, isActive, ct);
    }

    private void EnsureEnvReferencesAllowed(Provider provider)
    {
        if (provider.Type != ProviderType.Http)
            return;
        var settings = HttpProviderSettings.FromSettings(provider.Settings);
        _envPolicy.EnsureAllowed(settings.ApiKeyEnv, "API key variable");
        _envPolicy.EnsureAllowed(settings.EndpointEnvOverride, "Endpoint override variable");
    }
}
