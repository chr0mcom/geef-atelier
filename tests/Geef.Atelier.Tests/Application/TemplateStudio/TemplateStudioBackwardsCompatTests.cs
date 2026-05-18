using Geef.Atelier.Core.Domain.Crew.TemplateStudio;
using Geef.Atelier.Mcp.Tools;

namespace Geef.Atelier.Tests.Application.TemplateStudio;

/// <summary>
/// Regression tests: old tool inputs (without Reasoning fields, without executor profile_type)
/// must still deserialize correctly. ProposedProfileDto and ProposedTemplateDto have nullable
/// defaults, so old JSON that omits them should deserialize to null fields without error.
/// </summary>
public sealed class TemplateStudioBackwardsCompatTests
{
    [Fact]
    public void ProposedTemplateDto_OldInputWithoutReasoning_DeserializesCorrectly()
    {
        var dto = new ProposedTemplateDto(
            Name: "old-template",
            DisplayName: "Old Template",
            Description: "Legacy.",
            ExecutorProfileName: "default-executor",
            ReviewerProfileNames: ["briefing-fidelity"],
            AdvisorProfileNames: [],
            GroundingProviderProfileNames: [],
            EvaluationStrategy: "Sequential"
            // EvaluationStrategyReasoning defaults to null
        );

        Assert.Null(dto.EvaluationStrategyReasoning);
        Assert.Equal("Sequential", dto.EvaluationStrategy);
    }

    [Fact]
    public void ProposedProfileDto_OldInputWithoutReasoningFields_DeserializesCorrectly()
    {
        var dto = new ProposedProfileDto(
            ProfileType: "reviewer",
            Name: "old-reviewer",
            DisplayName: "Old Reviewer",
            Description: "Legacy.",
            Model: "gpt-4o-mini",
            Provider: "openrouter",
            SystemPrompt: "Review carefully.",
            MaxTokens: null,
            ReviewerFocus: null,
            AdvisorMode: null,
            AdvisorTrigger: null,
            GroundingProviderType: null,
            GroundingProviderSettings: null
            // Reasoning fields default to null
        );

        Assert.Null(dto.ModelReasoning);
        Assert.Null(dto.SystemPromptReasoning);
        Assert.Null(dto.OverallReasoning);
        Assert.Null(dto.ModeReasoning);
        Assert.Null(dto.TriggerReasoning);
    }

    [Fact]
    public void ProposedProfile_OldRecordWithoutReasoningFields_DeserializesCorrectly()
    {
        // Simulates reading a ProposedProfile that was stored before Reasoning fields were added
        var profile = new ProposedProfile(
            ProfileType: ProposedProfileType.Reviewer,
            Name: "old-reviewer",
            DisplayName: "Old Reviewer",
            Description: "Legacy.",
            Model: "gpt-4o-mini",
            Provider: "openrouter",
            SystemPrompt: "Review carefully.",
            MaxTokens: null,
            ReviewerFocus: null,
            AdvisorMode: null,
            AdvisorTrigger: null,
            GroundingProviderType: null,
            GroundingProviderSettings: null
            // All Reasoning fields default to null
        );

        Assert.Null(profile.ModelReasoning);
        Assert.Null(profile.SystemPromptReasoning);
        Assert.Null(profile.OverallReasoning);
        Assert.Null(profile.ModeReasoning);
        Assert.Null(profile.TriggerReasoning);
    }

    [Fact]
    public void ProposedTemplate_OldRecordWithoutReasoningField_DeserializesCorrectly()
    {
        var template = new ProposedTemplate(
            Name: "old-template",
            DisplayName: "Old Template",
            Description: "Legacy.",
            ExecutorProfileName: "default-executor",
            ReviewerProfileNames: ["briefing-fidelity"],
            AdvisorProfileNames: [],
            GroundingProviderProfileNames: [],
            EvaluationStrategy: "Sequential"
            // EvaluationStrategyReasoning defaults to null
        );

        Assert.Null(template.EvaluationStrategyReasoning);
    }
}
