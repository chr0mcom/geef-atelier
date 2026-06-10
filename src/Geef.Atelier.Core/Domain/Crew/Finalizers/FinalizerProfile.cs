namespace Geef.Atelier.Core.Domain.Crew.Finalizers;

/// <summary>
/// Describes how a single finalizer step should execute after the crew converges.
/// System profiles are code constants; custom profiles are persisted in the database
/// under the <c>"custom-"</c> name prefix.
/// </summary>
/// <param name="Name">Unique identifier. System profiles use plain names; custom profiles carry a <c>"custom-"</c> prefix.</param>
/// <param name="DisplayName">Human-readable label surfaced in the UI.</param>
/// <param name="Description">One- or two-sentence summary of what this finalizer does.</param>
/// <param name="FinalizerType">Discriminator used by <c>IFinalizerExecutorFactory</c> to select the correct executor.</param>
/// <param name="Settings">
/// Type-specific configuration as string key/value pairs.
/// Keys depend on <see cref="FinalizerType"/>; see the typed settings records for valid keys.
/// </param>
/// <param name="IsSystem">True for code-constant profiles defined in <see cref="SystemCrew"/>.</param>
/// <param name="CreatedAt">UTC timestamp of when the profile was first persisted. Null for system profiles.</param>
/// <param name="UpdatedAt">UTC timestamp of the last update. Null for system profiles.</param>
/// <param name="ToolNames">
/// Optional list of tool names (from the tool catalogue) that a <see cref="FinalizerType.Transform"/>
/// finalizer may call during an agentic tool-use loop. Ignored for all other finalizer types.
/// </param>
public sealed record FinalizerProfile(
    string Name,
    string DisplayName,
    string Description,
    FinalizerType FinalizerType,
    Dictionary<string, string> Settings,
    bool IsSystem,
    DateTimeOffset? CreatedAt = null,
    DateTimeOffset? UpdatedAt = null,
    IReadOnlyList<string>? ToolNames = null);
