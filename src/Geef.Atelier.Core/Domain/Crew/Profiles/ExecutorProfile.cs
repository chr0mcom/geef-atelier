namespace Geef.Atelier.Core.Domain.Crew.Profiles;

/// <summary>
/// Configuration for the executor (drafting LLM) in the Atelier pipeline. The executor produces
/// the initial draft and revises it across iterations based on reviewer findings.
/// </summary>
/// <param name="Name">
/// Unique kebab-case identifier (e.g. <c>"default-executor"</c>). System profiles use stable
/// short slugs; user-created profiles are auto-prefixed with <c>"custom-"</c> to prevent collisions.
/// </param>
/// <param name="DisplayName">Human-readable name surfaced in the UI.</param>
/// <param name="Description">One- or two-sentence summary of the executor's role.</param>
/// <param name="SystemPrompt">The full system prompt that drives the executor's drafting behaviour.</param>
/// <param name="Provider">Reference to a key in <c>LlmOptions.Providers</c>.</param>
/// <param name="Model">Bare model identifier passed to the provider.</param>
/// <param name="MaxTokens">Optional per-profile token cap; falls back to <c>LlmOptions.DefaultMaxTokens</c> when null.</param>
/// <param name="IsSystem">
/// True for profiles defined as code constants in <see cref="SystemCrew"/>; false for user-created
/// profiles persisted in the database. System profiles are read-only at runtime.
/// </param>
/// <param name="ToolNames">
/// Optional list of tool names (from the tool catalogue) that this executor may call during an
/// agentic tool-use loop. When empty the executor uses the standard single-shot completion path.
/// </param>
public sealed record ExecutorProfile(
    string Name,
    string DisplayName,
    string Description,
    string SystemPrompt,
    string Provider,
    string Model,
    int? MaxTokens,
    bool IsSystem,
    IReadOnlyList<string>? ToolNames = null);
