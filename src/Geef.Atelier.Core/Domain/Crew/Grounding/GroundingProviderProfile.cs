namespace Geef.Atelier.Core.Domain.Crew.Grounding;

/// <summary>
/// Describes how a single grounding provider should be invoked for a run.
/// System profiles are code constants; custom profiles are persisted in the database
/// under the <c>"custom-"</c> name prefix.
/// </summary>
/// <param name="Name">Unique identifier. System profiles use plain names; custom profiles carry a <c>"custom-"</c> prefix.</param>
/// <param name="DisplayName">Human-readable label surfaced in the UI.</param>
/// <param name="Description">One- or two-sentence summary of the provider's purpose.</param>
/// <param name="ProviderType">Discriminator string used by <c>IGroundingProviderFactory</c> (e.g. <c>"tavily"</c>).</param>
/// <param name="ProviderSettings">
/// Provider-specific configuration as string key/value pairs.
/// For Tavily: <c>Tier</c> (basic|advanced), <c>MaxResults</c>, <c>IncludeAnswer</c>.
/// Designed to be extensible for a future <c>VectorStoreGroundingProvider</c> without a schema change.
/// </param>
/// <param name="MaxQueriesPerRun">Safety cap on provider calls per run. <c>null</c> means 1 (default).</param>
/// <param name="IsSystem">True for code-constant profiles defined in <see cref="SystemCrew"/>.</param>
public sealed record GroundingProviderProfile(
    string Name,
    string DisplayName,
    string Description,
    string ProviderType,
    Dictionary<string, string> ProviderSettings,
    int? MaxQueriesPerRun,
    bool IsSystem);
