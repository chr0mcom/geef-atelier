using Geef.Atelier.Application.Crew.TemplateStudio;
using Geef.Atelier.Core.Domain.Crew.TemplateStudio;
using Geef.Atelier.Mcp.Tools;

namespace Geef.Atelier.Tests.Mcp;

public sealed class AnalyzeTemplateProposalToolTests
{
    // Stub for ITemplateStudioService — only AnalyzeAsync is called in these tests.
    private sealed class StubStudioService(TemplateStudioAnalysis response) : ITemplateStudioService
    {
        public Task<TemplateStudioAnalysis> AnalyzeAsync(string taskDescription, CancellationToken ct = default)
            => Task.FromResult(response);

        public Task<TemplateStudioAnalysis> AnalyzeAsync(string taskDescription, StudioModelChoice? overrideChoice, CancellationToken ct = default)
            => Task.FromResult(response);

        public Task<TemplateStudioAnalysis> AnalyzeAsync(string taskDescription, StudioModelChoice? overrideChoice, IProgress<string>? progress, CancellationToken ct = default)
            => Task.FromResult(response);

        public Task<StudioModelChoice> GetEffectiveDefaultAsync(CancellationToken ct = default)
            => Task.FromResult(new StudioModelChoice("openrouter", "test-model", 8192));

        public Task SaveDefaultAsync(StudioModelChoice choice, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<MaterializationResult> MaterializeAsync(Guid analysisId, MaterializationRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<StudioAnalysesPage> ListRecentAnalysesAsync(int page, int pageSize, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private static TemplateStudioAnalysis BuildAnalysis(
        ProposedTemplate? proposedTemplate = null,
        IReadOnlyList<ProposedProfile>? proposedProfiles = null) => new(
        Id: Guid.NewGuid(),
        TaskDescription: "Test task",
        MatchedExistingTemplates: [new TemplateMatch("klassik", 0.9, "Good match")],
        Recommendation: StudioRecommendation.CreateNewTemplate,
        ProposedTemplate: proposedTemplate,
        ProposedNewProfiles: proposedProfiles ?? [],
        ReasoningSummary: "Reasoning here",
        InputTokens: 100,
        OutputTokens: 50,
        CostEur: 0.02m,
        CreatedAt: DateTimeOffset.UtcNow);

    [Fact]
    public async Task AnalyzeTemplateProposal_MapsAnalysisIdAndDescription()
    {
        var analysis = BuildAnalysis();
        var svc = new StubStudioService(analysis);

        var output = await AnalyzeTemplateProposalTool.AnalyzeTemplateProposal(svc, "Test task");

        Assert.Equal(analysis.Id, output.AnalysisId);
        Assert.Equal("Test task", output.TaskDescription);
    }

    [Fact]
    public async Task AnalyzeTemplateProposal_MapsRecommendationAsString()
    {
        var analysis = BuildAnalysis();
        var svc = new StubStudioService(analysis);

        var output = await AnalyzeTemplateProposalTool.AnalyzeTemplateProposal(svc, "Test task");

        Assert.Equal("CreateNewTemplate", output.Recommendation);
    }

    [Fact]
    public async Task AnalyzeTemplateProposal_MapsMatchedTemplates()
    {
        var analysis = BuildAnalysis();
        var svc = new StubStudioService(analysis);

        var output = await AnalyzeTemplateProposalTool.AnalyzeTemplateProposal(svc, "Test task");

        Assert.Single(output.MatchedExistingTemplates);
        var match = output.MatchedExistingTemplates[0];
        Assert.Equal("klassik", match.TemplateName);
        Assert.Equal(0.9, match.Confidence);
        Assert.Equal("Good match", match.Reasoning);
    }

    [Fact]
    public async Task AnalyzeTemplateProposal_MapsTokensAndCost()
    {
        var analysis = BuildAnalysis();
        var svc = new StubStudioService(analysis);

        var output = await AnalyzeTemplateProposalTool.AnalyzeTemplateProposal(svc, "Test task");

        Assert.Equal(100, output.InputTokens);
        Assert.Equal(50, output.OutputTokens);
        Assert.Equal(0.02m, output.CostEur);
    }

    [Fact]
    public async Task AnalyzeTemplateProposal_MapsReasoningSummary()
    {
        var analysis = BuildAnalysis();
        var svc = new StubStudioService(analysis);

        var output = await AnalyzeTemplateProposalTool.AnalyzeTemplateProposal(svc, "Test task");

        Assert.Equal("Reasoning here", output.ReasoningSummary);
    }

    [Fact]
    public async Task AnalyzeTemplateProposal_MapsProposedTemplate_WhenPresent()
    {
        var template = new ProposedTemplate(
            "my-template", "My Template", "Desc",
            "default-executor", ["reviewer-1"], [], [], "Parallel");
        var analysis = BuildAnalysis(proposedTemplate: template);
        var svc = new StubStudioService(analysis);

        var output = await AnalyzeTemplateProposalTool.AnalyzeTemplateProposal(svc, "Test task");

        Assert.NotNull(output.ProposedTemplate);
        Assert.Equal("my-template", output.ProposedTemplate!.Name);
        Assert.Equal("Parallel", output.ProposedTemplate.EvaluationStrategy);
        Assert.Equal("default-executor", output.ProposedTemplate.ExecutorProfileName);
    }

    [Fact]
    public async Task AnalyzeTemplateProposal_NullProposedTemplate_WhenNotPresent()
    {
        var analysis = BuildAnalysis(proposedTemplate: null);
        var svc = new StubStudioService(analysis);

        var output = await AnalyzeTemplateProposalTool.AnalyzeTemplateProposal(svc, "Test task");

        Assert.Null(output.ProposedTemplate);
    }

    [Fact]
    public async Task AnalyzeTemplateProposal_EmptyProposedProfiles_ReturnsOutput()
    {
        var analysis = BuildAnalysis(proposedProfiles: []);
        var svc = new StubStudioService(analysis);

        var output = await AnalyzeTemplateProposalTool.AnalyzeTemplateProposal(svc, "Test task");

        Assert.Empty(output.ProposedNewProfiles);
    }

    [Fact]
    public async Task AnalyzeTemplateProposal_MapsProposedNewProfiles_WhenPresent()
    {
        var profiles = new List<ProposedProfile>
        {
            new(
                ProfileType: ProposedProfileType.Reviewer,
                Name: "my-reviewer",
                DisplayName: "My Reviewer",
                Description: "Reviews quality.",
                Model: "gpt-4o-mini",
                Provider: "openai",
                SystemPrompt: "You review.",
                MaxTokens: null,
                ReviewerFocus: "clarity",
                AdvisorMode: null,
                AdvisorTrigger: null,
                GroundingProviderType: null,
                GroundingProviderSettings: null)
        };
        var analysis = BuildAnalysis(proposedProfiles: profiles);
        var svc = new StubStudioService(analysis);

        var output = await AnalyzeTemplateProposalTool.AnalyzeTemplateProposal(svc, "Test task");

        Assert.Single(output.ProposedNewProfiles);
        var profile = output.ProposedNewProfiles[0];
        Assert.Equal("Reviewer", profile.ProfileType);
        Assert.Equal("my-reviewer", profile.Name);
        Assert.Equal("clarity", profile.ReviewerFocus);
    }
}
