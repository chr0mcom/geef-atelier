namespace Geef.Atelier.Core.Domain.Crew;

/// <summary>Where the model list currently served for a provider came from.</summary>
public enum ModelCatalogSource
{
    /// <summary>No fetch has been attempted for this provider yet.</summary>
    Unknown = 0,

    /// <summary>The list was resolved successfully from the provider's live models endpoint.</summary>
    Live = 1,

    /// <summary>
    /// The provider has no live models source by design, so the static list is authoritative
    /// rather than a degradation. Reporting this separately keeps the UI from claiming that a
    /// provider is unreachable when it was never meant to be reachable.
    /// </summary>
    StaticByDesign = 2,

    /// <summary>A live lookup was attempted and failed; the static list is a degraded substitute.</summary>
    Fallback = 3,
}
