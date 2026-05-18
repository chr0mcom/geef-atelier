namespace Geef.Atelier.Core.Domain.Crew.TemplateStudio;

/// <summary>A crew template proposed by the Template Studio meta-LLM, pending user review before persistence.</summary>
public sealed record ProposedTemplate(
    string Name,
    string DisplayName,
    string Description,
    string ExecutorProfileName,
    IReadOnlyList<string> ReviewerProfileNames,
    IReadOnlyList<string> AdvisorProfileNames,
    IReadOnlyList<string> GroundingProviderProfileNames,
    string EvaluationStrategy,
    string? EvaluationStrategyReasoning = null);
