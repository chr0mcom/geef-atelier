using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Application.Crew;

/// <summary>Lists available models per provider, with transparent caching and static fallback.</summary>
public interface IModelCatalog
{
    /// <summary>
    /// Returns the available models for <paramref name="providerName"/>.
    /// Results are cached for 24 hours. Falls back to <see cref="StaticModelFallback"/> when the
    /// provider endpoint is unreachable and no cached data exists.
    /// </summary>
    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(string providerName, CancellationToken ct = default);

    /// <summary>Invalidates the cached list and re-fetches from the provider endpoint.</summary>
    Task<IReadOnlyList<ModelInfo>> RefreshAsync(string providerName, CancellationToken ct = default);

    /// <summary>True if the last fetch for <paramref name="providerName"/> used the static fallback list.</summary>
    bool IsUsingFallback(string providerName);
}
