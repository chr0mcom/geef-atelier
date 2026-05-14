using Geef.Atelier.Core.Domain.Crew.TemplateStudio;

namespace Geef.Atelier.Tests.Domain.TemplateStudio;

public sealed class TemplateStudioAnalysisTests
{
    [Fact]
    public void TemplateStudioAnalysis_Construction_SetsAllFields()
    {
        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        var matches = new List<TemplateMatch> { new("klassik", 0.9, "Fits well.") };
        var profiles = new List<ProposedProfile>();
        var template = new ProposedTemplate("my-template", "My Template", "Desc",
            "default-executor", ["briefing-fidelity"], [], [], "Sequential");

        var analysis = new TemplateStudioAnalysis(
            Id: id,
            TaskDescription: "Write a report.",
            MatchedExistingTemplates: matches,
            Recommendation: StudioRecommendation.UseExistingTemplate,
            ProposedTemplate: template,
            ProposedNewProfiles: profiles,
            ReasoningSummary: "Use existing template.",
            InputTokens: 100,
            OutputTokens: 50,
            CostEur: 0.01m,
            CreatedAt: createdAt);

        Assert.Equal(id, analysis.Id);
        Assert.Equal("Write a report.", analysis.TaskDescription);
        Assert.Single(analysis.MatchedExistingTemplates);
        Assert.Equal(StudioRecommendation.UseExistingTemplate, analysis.Recommendation);
        Assert.NotNull(analysis.ProposedTemplate);
        Assert.Empty(analysis.ProposedNewProfiles);
        Assert.Equal("Use existing template.", analysis.ReasoningSummary);
        Assert.Equal(100, analysis.InputTokens);
        Assert.Equal(50, analysis.OutputTokens);
        Assert.Equal(0.01m, analysis.CostEur);
        Assert.Equal(createdAt, analysis.CreatedAt);
    }

    [Fact]
    public void TemplateStudioAnalysis_NullCostEur_IsAllowed()
    {
        var analysis = new TemplateStudioAnalysis(
            Id: Guid.NewGuid(),
            TaskDescription: "Task",
            MatchedExistingTemplates: [],
            Recommendation: StudioRecommendation.CreateNewTemplate,
            ProposedTemplate: null,
            ProposedNewProfiles: [],
            ReasoningSummary: "No match.",
            InputTokens: 10,
            OutputTokens: 5,
            CostEur: null,
            CreatedAt: DateTimeOffset.UtcNow);

        Assert.Null(analysis.CostEur);
        Assert.Null(analysis.ProposedTemplate);
    }

    [Fact]
    public void TemplateMatch_Construction_SetsAllFields()
    {
        var match = new TemplateMatch("klassik", 0.95, "Very good fit.");

        Assert.Equal("klassik", match.TemplateName);
        Assert.Equal(0.95, match.Confidence);
        Assert.Equal("Very good fit.", match.Reasoning);
    }

    [Fact]
    public void ProposedProfile_WithNullOptionalFields_IsValid()
    {
        var profile = new ProposedProfile(
            ProfileType: ProposedProfileType.Reviewer,
            Name: "my-reviewer",
            DisplayName: "My Reviewer",
            Description: "Reviews text.",
            Model: "gpt-4o-mini",
            Provider: "openrouter",
            SystemPrompt: "You are a reviewer.",
            MaxTokens: null,
            ReviewerFocus: null,
            AdvisorMode: null,
            AdvisorTrigger: null,
            GroundingProviderType: null,
            GroundingProviderSettings: null);

        Assert.Equal(ProposedProfileType.Reviewer, profile.ProfileType);
        Assert.Equal("my-reviewer", profile.Name);
        Assert.Null(profile.MaxTokens);
        Assert.Null(profile.ReviewerFocus);
        Assert.Null(profile.AdvisorMode);
        Assert.Null(profile.AdvisorTrigger);
        Assert.Null(profile.GroundingProviderType);
        Assert.Null(profile.GroundingProviderSettings);
    }

    [Fact]
    public void ProposedProfile_WithAllOptionalFields_SetsAll()
    {
        var settings = new Dictionary<string, string> { ["Tier"] = "basic" };

        var profile = new ProposedProfile(
            ProfileType: ProposedProfileType.GroundingProvider,
            Name: "my-grounding",
            DisplayName: "My Grounding",
            Description: "Fetches external data.",
            Model: "n/a",
            Provider: "tavily",
            SystemPrompt: "Search the web.",
            MaxTokens: 2048,
            ReviewerFocus: "factual accuracy",
            AdvisorMode: "Strategic",
            AdvisorTrigger: "BeforeFirstExecution",
            GroundingProviderType: "tavily",
            GroundingProviderSettings: settings);

        Assert.Equal(2048, profile.MaxTokens);
        Assert.Equal("factual accuracy", profile.ReviewerFocus);
        Assert.Equal("Strategic", profile.AdvisorMode);
        Assert.Equal("BeforeFirstExecution", profile.AdvisorTrigger);
        Assert.Equal("tavily", profile.GroundingProviderType);
        Assert.Equal("basic", profile.GroundingProviderSettings!["Tier"]);
    }

    [Fact]
    public void ProposedTemplate_Construction_SetsAllFields()
    {
        var template = new ProposedTemplate(
            Name: "legal-review",
            DisplayName: "Legal Review",
            Description: "Reviews legal contracts.",
            ExecutorProfileName: "default-executor",
            ReviewerProfileNames: ["legal-risk", "clarity"],
            AdvisorProfileNames: ["briefing-clarifier"],
            GroundingProviderProfileNames: ["tavily-basic"],
            EvaluationStrategy: "Parallel");

        Assert.Equal("legal-review", template.Name);
        Assert.Equal("Legal Review", template.DisplayName);
        Assert.Equal("Reviews legal contracts.", template.Description);
        Assert.Equal("default-executor", template.ExecutorProfileName);
        Assert.Equal(["legal-risk", "clarity"], template.ReviewerProfileNames);
        Assert.Equal(["briefing-clarifier"], template.AdvisorProfileNames);
        Assert.Equal(["tavily-basic"], template.GroundingProviderProfileNames);
        Assert.Equal("Parallel", template.EvaluationStrategy);
    }
}
