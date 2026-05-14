namespace Geef.Atelier.Core.Domain.Crew.TemplateStudio;

/// <summary>
/// Result of a Template Studio meta-LLM analysis. Captures both the raw analysis (for the audit trail)
/// and the structured proposal the user reviews before materialization.
/// </summary>
public sealed record TemplateStudioAnalysis(
    Guid Id,
    string TaskDescription,
    IReadOnlyList<TemplateMatch> MatchedExistingTemplates,
    StudioRecommendation Recommendation,
    ProposedTemplate? ProposedTemplate,
    IReadOnlyList<ProposedProfile> ProposedNewProfiles,
    string ReasoningSummary,
    int InputTokens,
    int OutputTokens,
    decimal? CostEur,
    DateTimeOffset CreatedAt);
