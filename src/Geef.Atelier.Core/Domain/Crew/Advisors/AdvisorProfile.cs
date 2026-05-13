namespace Geef.Atelier.Core.Domain.Crew.Advisors;

/// <summary>
/// Configuration for an advisor consulted during grounding or finalisation. Stub schema for PS-7;
/// no advisor pass is functional in PS-5. The shape mirrors <c>ReviewerProfile</c> with an extra
/// <see cref="Mode"/> discriminator so PS-7 can wire advisors without schema changes.
/// </summary>
/// <param name="Name">Unique kebab-case identifier; user-created profiles are auto-prefixed with <c>"custom-"</c>.</param>
/// <param name="DisplayName">Human-readable name surfaced in the UI.</param>
/// <param name="Description">One- or two-sentence summary of the advisor's role.</param>
/// <param name="SystemPrompt">The full system prompt that drives the advisor's behaviour.</param>
/// <param name="Provider">Reference to a key in <c>LlmOptions.Providers</c>.</param>
/// <param name="Model">Bare model identifier passed to the provider.</param>
/// <param name="MaxTokens">Optional per-profile token cap; falls back to <c>LlmOptions.DefaultMaxTokens</c> when null.</param>
/// <param name="Mode">Behavioural archetype that determines how the response is integrated.</param>
/// <param name="Trigger">When during the pipeline run the advisor is consulted.</param>
/// <param name="IsSystem">True for profiles defined as code constants; false for user-created profiles.</param>
public sealed record AdvisorProfile(
    string Name,
    string DisplayName,
    string Description,
    string SystemPrompt,
    string Provider,
    string Model,
    int? MaxTokens,
    AdvisorMode Mode,
    AdvisorTrigger Trigger,
    bool IsSystem);
