namespace Geef.Atelier.Core.Domain.Crew.Profiles;

/// <summary>
/// Configuration for a single reviewer in the Atelier pipeline. Reviewers are stateless
/// LLM actors that inspect a draft and emit findings via the <c>submit_review</c> tool.
/// </summary>
/// <param name="Name">
/// Unique kebab-case identifier (e.g. <c>"briefing-fidelity"</c>). System profiles use stable
/// short slugs; user-created profiles are auto-prefixed with <c>"custom-"</c> to prevent collisions.
/// </param>
/// <param name="DisplayName">Human-readable name surfaced in the UI.</param>
/// <param name="Description">One- or two-sentence summary of the reviewer's role.</param>
/// <param name="SystemPrompt">The full system prompt that drives the reviewer's behaviour.</param>
/// <param name="Provider">Reference to a key in <c>LlmOptions.Providers</c>.</param>
/// <param name="Model">Bare model identifier passed to the provider (e.g. <c>"google/gemini-2.5-flash"</c>).</param>
/// <param name="MaxTokens">Optional per-profile token cap; falls back to <c>LlmOptions.DefaultMaxTokens</c> when null.</param>
/// <param name="IsSystem">
/// True for profiles defined as code constants in <see cref="SystemCrew"/>; false for user-created
/// profiles persisted in the database. System profiles are read-only at runtime.
/// </param>
public sealed record ReviewerProfile(
    string Name,
    string DisplayName,
    string Description,
    string SystemPrompt,
    string Provider,
    string Model,
    int? MaxTokens,
    bool IsSystem);
