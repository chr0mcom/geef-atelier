namespace Geef.Atelier.Core.Persistence.Providers;

using Geef.Atelier.Core.Domain.Providers;

/// <summary>Persistence access for <see cref="Provider"/> records (system + custom).</summary>
public interface IProviderRepository
{
    /// <summary>Returns all providers, optionally including inactive ones.</summary>
    Task<IReadOnlyList<Provider>> ListAsync(bool includeInactive, CancellationToken ct);

    /// <summary>Returns the provider with the given name, or <c>null</c> if not found.</summary>
    Task<Provider?> GetByNameAsync(string name, CancellationToken ct);

    /// <summary>Persists a new provider.</summary>
    Task CreateAsync(Provider provider, CancellationToken ct);

    /// <summary>Replaces an existing provider's mutable fields. Throws if not found.</summary>
    Task UpdateAsync(Provider provider, CancellationToken ct);

    /// <summary>Deletes a provider by name. Throws if not found.</summary>
    Task DeleteAsync(string name, CancellationToken ct);

    /// <summary>Toggles the active state of a provider without a full update. Throws if not found.</summary>
    Task SetActiveAsync(string name, bool isActive, CancellationToken ct);

    /// <summary>
    /// Returns true when any reviewer, executor, advisor, or finalizer profile references
    /// <paramref name="name"/> as its provider. Used to guard delete operations.
    /// </summary>
    Task<bool> IsReferencedByAnyProfileAsync(string name, CancellationToken ct);
}
