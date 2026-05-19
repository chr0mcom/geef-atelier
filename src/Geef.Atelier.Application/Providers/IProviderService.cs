namespace Geef.Atelier.Application.Providers;

using Geef.Atelier.Core.Domain.Providers;

/// <summary>
/// Application-service contract for managing LLM provider registrations.
/// System providers are code constants in <see cref="SystemProviders"/>; custom providers
/// are persisted in the database under the <c>"custom-"</c> name prefix.
/// </summary>
public interface IProviderService
{
    /// <summary>
    /// Returns all providers. System providers are always included. Custom providers are filtered
    /// by <see cref="Provider.IsActive"/> unless <paramref name="includeInactive"/> is true.
    /// </summary>
    Task<IReadOnlyList<Provider>> ListAsync(bool includeInactive = false, CancellationToken ct = default);

    /// <summary>Returns the provider with the given name (system or custom), or null if not found.</summary>
    Task<Provider?> GetByNameAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Persists a new custom provider. The name is auto-prefixed with <c>"custom-"</c> if absent.
    /// Throws <see cref="InvalidOperationException"/> when <paramref name="provider"/> carries a system name.
    /// </summary>
    Task<Provider> CreateCustomAsync(Provider provider, CancellationToken ct = default);

    /// <summary>
    /// Replaces the mutable fields of an existing custom provider identified by <paramref name="name"/>.
    /// Throws <see cref="InvalidOperationException"/> for system providers.
    /// </summary>
    Task<Provider> UpdateCustomAsync(string name, Provider provider, CancellationToken ct = default);

    /// <summary>
    /// Deletes a custom provider by name. Throws <see cref="InvalidOperationException"/> for system
    /// providers or when the provider is still referenced by crew profiles.
    /// </summary>
    Task DeleteCustomAsync(string name, CancellationToken ct = default);

    /// <summary>Toggles the active state of a provider. No-op for system providers (always active).</summary>
    Task SetActiveAsync(string name, bool isActive, CancellationToken ct = default);

    /// <summary>
    /// Performs a live connectivity test against the provider's endpoint.
    /// Returns a <see cref="ConnectionTestResult"/> with latency and an optional response sample.
    /// CLI providers always return a synthetic success result.
    /// </summary>
    Task<ConnectionTestResult> TestConnectionAsync(string name, CancellationToken ct = default);
}
