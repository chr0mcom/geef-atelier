using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Application.Crew;

/// <summary>Lists available models per provider, with transparent caching and static fallback.</summary>
public interface IModelCatalog
{
    /// <summary>
    /// Returns the available models for <paramref name="providerName"/>, serving a cached list when
    /// one is present. Uses the short interactive budget, so a cold backend degrades to the static
    /// list instead of blocking the caller.
    /// </summary>
    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(string providerName, CancellationToken ct = default);

    /// <summary>
    /// Invalidates the cached list and re-fetches, asking the backend to re-resolve rather than
    /// serve its own cache. Uses its own refresh budget — larger than the interactive one because a
    /// re-resolution costs real work upstream, smaller than the warm-up one because someone is
    /// waiting. Intended for the user-triggered refresh button.
    /// </summary>
    Task<IReadOnlyList<ModelInfo>> RefreshAsync(string providerName, CancellationToken ct = default);

    /// <summary>
    /// Invalidates the cached list and re-fetches with the long background budget, tolerating a cold
    /// backend. Intended for the nightly warm-up only — never call this from an interactive path.
    /// </summary>
    Task<IReadOnlyList<ModelInfo>> WarmUpAsync(string providerName, CancellationToken ct = default);

    /// <summary>Where the list most recently served for <paramref name="providerName"/> came from.</summary>
    ModelCatalogSource GetSource(string providerName);

    /// <summary>
    /// True when the last lookup for <paramref name="providerName"/> failed and the static list is a
    /// degraded substitute. Equivalent to <see cref="GetSource"/> returning
    /// <see cref="ModelCatalogSource.Fallback"/>; providers without a live source report false.
    /// </summary>
    bool IsUsingFallback(string providerName);

    /// <summary>
    /// Resolves an always-latest alias such as <c>claude-opus-latest</c> to the concrete model it
    /// currently points at, so a stored run records which model actually ran rather than a moving
    /// target. Returns <paramref name="modelId"/> unchanged when no mapping is known. Never throws.
    /// </summary>
    Task<string> ResolveModelAsync(string providerName, string modelId, CancellationToken ct = default);
}
