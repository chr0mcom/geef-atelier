namespace Geef.Atelier.Core.Domain.Crew.TemplateStudio;

/// <summary>
/// A crew profile proposed by the Template Studio meta-LLM.
/// Type-specific fields are non-null only for the matching <see cref="ProfileType"/>.
/// </summary>
public sealed record ProposedProfile(
    ProposedProfileType ProfileType,
    string Name,
    string DisplayName,
    string Description,
    string Model,
    string Provider,
    string SystemPrompt,
    int? MaxTokens,
    // Reviewer-specific
    string? ReviewerFocus,
    // Advisor-specific
    string? AdvisorMode,
    string? AdvisorTrigger,
    // Grounding-specific
    string? GroundingProviderType,
    Dictionary<string, string>? GroundingProviderSettings);
