using System.ComponentModel;
using Geef.Atelier.Application.Crew.TemplateStudio;
using Geef.Atelier.Core.Domain.Crew.TemplateStudio;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

[McpServerToolType]
public static class AnalyzeTemplateProposalTool
{
    [McpServerTool, Description(
        "Analyses a task description using a meta-LLM call and returns a structured crew configuration proposal. " +
        "Returns matched existing templates, a recommendation (UseExistingTemplate/CreateNewTemplate/AdaptExistingTemplate), " +
        "a proposed new template with profiles, and reasoning. " +
        "Use materialize_template_proposal to persist the proposal after optional user review and editing. " +
        "Example task: 'Write a press release announcing our Q2 results for a B2B audience.'")]
    public static async Task<AnalyzeTemplateProposalOutput> AnalyzeTemplateProposal(
        ITemplateStudioService studioService,
        [Description("Description of the task the crew should be configured for. Be specific about audience, format, and goals.")] string taskDescription,
        CancellationToken cancellationToken = default)
    {
        var analysis = await studioService.AnalyzeAsync(taskDescription, cancellationToken);
        return MapToOutput(analysis);
    }

    private static AnalyzeTemplateProposalOutput MapToOutput(TemplateStudioAnalysis a) => new(
        AnalysisId: a.Id,
        TaskDescription: a.TaskDescription,
        MatchedExistingTemplates: a.MatchedExistingTemplates
            .Select(m => new TemplateMatchDto(m.TemplateName, m.Confidence, m.Reasoning))
            .ToList(),
        Recommendation: a.Recommendation.ToString(),
        ProposedTemplate: a.ProposedTemplate is null ? null : MapTemplate(a.ProposedTemplate),
        ProposedNewProfiles: a.ProposedNewProfiles.Select(MapProfile).ToList(),
        ReasoningSummary: a.ReasoningSummary,
        InputTokens: a.InputTokens,
        OutputTokens: a.OutputTokens,
        CostEur: a.CostEur,
        CreatedAt: a.CreatedAt);

    private static ProposedTemplateDto MapTemplate(ProposedTemplate t) => new(
        t.Name, t.DisplayName, t.Description, t.ExecutorProfileName,
        t.ReviewerProfileNames, t.AdvisorProfileNames, t.GroundingProviderProfileNames,
        t.EvaluationStrategy, t.EvaluationStrategyReasoning);

    private static ProposedProfileDto MapProfile(ProposedProfile p) => new(
        p.ProfileType.ToString(), p.Name, p.DisplayName, p.Description,
        p.Model, p.Provider, p.SystemPrompt, p.MaxTokens,
        p.ReviewerFocus, p.AdvisorMode, p.AdvisorTrigger,
        p.GroundingProviderType, p.GroundingProviderSettings,
        p.ModelReasoning, p.SystemPromptReasoning, p.OverallReasoning,
        p.ModeReasoning, p.TriggerReasoning);
}

public sealed record AnalyzeTemplateProposalOutput(
    Guid AnalysisId,
    string TaskDescription,
    IReadOnlyList<TemplateMatchDto> MatchedExistingTemplates,
    string Recommendation,
    ProposedTemplateDto? ProposedTemplate,
    IReadOnlyList<ProposedProfileDto> ProposedNewProfiles,
    string ReasoningSummary,
    int InputTokens,
    int OutputTokens,
    decimal? CostEur,
    DateTimeOffset CreatedAt);

public sealed record TemplateMatchDto(string TemplateName, double Confidence, string Reasoning);

public sealed record ProposedTemplateDto(
    string Name,
    string DisplayName,
    string Description,
    string ExecutorProfileName,
    IReadOnlyList<string> ReviewerProfileNames,
    IReadOnlyList<string> AdvisorProfileNames,
    IReadOnlyList<string> GroundingProviderProfileNames,
    string EvaluationStrategy,
    string? EvaluationStrategyReasoning = null);

public sealed record ProposedProfileDto(
    string ProfileType,
    string Name,
    string DisplayName,
    string Description,
    string Model,
    string Provider,
    string SystemPrompt,
    int? MaxTokens,
    string? ReviewerFocus,
    string? AdvisorMode,
    string? AdvisorTrigger,
    string? GroundingProviderType,
    Dictionary<string, string>? GroundingProviderSettings,
    string? ModelReasoning = null,
    string? SystemPromptReasoning = null,
    string? OverallReasoning = null,
    string? ModeReasoning = null,
    string? TriggerReasoning = null);
