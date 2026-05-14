using Geef.Atelier.Application.Crew.TemplateStudio;
using Geef.Atelier.Core.Domain.Crew.TemplateStudio;
using Geef.Atelier.Mcp.Tools;

namespace Geef.Atelier.Tests.Mcp;

public sealed class MaterializeTemplateProposalToolTests
{
    private sealed class CapturingStudioService : ITemplateStudioService
    {
        public MaterializationRequest? CapturedRequest { get; private set; }
        public Guid CapturedAnalysisId { get; private set; }

        public Task<MaterializationResult> MaterializeAsync(
            Guid analysisId, MaterializationRequest request, CancellationToken ct = default)
        {
            CapturedAnalysisId = analysisId;
            CapturedRequest = request;
            return Task.FromResult(new MaterializationResult("my-template", ["my-profile"], []));
        }

        public Task<TemplateStudioAnalysis> AnalyzeAsync(string taskDescription, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<StudioAnalysesPage> ListRecentAnalysesAsync(int page, int pageSize, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private static ProposedTemplateDto BuildTemplateDto() => new(
        "my-template", "My Template", "Description",
        "default-executor", ["reviewer-1"], [], [], "Parallel");

    [Fact]
    public async Task MaterializeTemplateProposal_ReturnsCreatedTemplateName()
    {
        var svc = new CapturingStudioService();
        var analysisId = Guid.NewGuid();

        var output = await MaterializeTemplateProposalTool.MaterializeTemplateProposal(
            svc, analysisId, BuildTemplateDto(), []);

        Assert.Equal("my-template", output.CreatedTemplateName);
    }

    [Fact]
    public async Task MaterializeTemplateProposal_PassesAnalysisIdToService()
    {
        var svc = new CapturingStudioService();
        var analysisId = Guid.NewGuid();

        await MaterializeTemplateProposalTool.MaterializeTemplateProposal(
            svc, analysisId, BuildTemplateDto(), []);

        Assert.Equal(analysisId, svc.CapturedAnalysisId);
    }

    [Fact]
    public async Task MaterializeTemplateProposal_MapsFinalTemplateCorrectly()
    {
        var svc = new CapturingStudioService();
        var dto = BuildTemplateDto();

        await MaterializeTemplateProposalTool.MaterializeTemplateProposal(
            svc, Guid.NewGuid(), dto, []);

        Assert.Equal("my-template", svc.CapturedRequest!.FinalTemplate.Name);
        Assert.Equal("default-executor", svc.CapturedRequest.FinalTemplate.ExecutorProfileName);
    }

    [Fact]
    public async Task MaterializeTemplateProposal_MapsFinalTemplateEvaluationStrategy()
    {
        var svc = new CapturingStudioService();

        await MaterializeTemplateProposalTool.MaterializeTemplateProposal(
            svc, Guid.NewGuid(), BuildTemplateDto(), []);

        Assert.Equal("Parallel", svc.CapturedRequest!.FinalTemplate.EvaluationStrategy);
    }

    [Fact]
    public async Task MaterializeTemplateProposal_EmptyProfiles_PassesEmptyList()
    {
        var svc = new CapturingStudioService();

        await MaterializeTemplateProposalTool.MaterializeTemplateProposal(
            svc, Guid.NewGuid(), BuildTemplateDto(), []);

        Assert.Empty(svc.CapturedRequest!.FinalNewProfiles);
    }

    [Fact]
    public async Task MaterializeTemplateProposal_MapsValidProfileType()
    {
        var svc = new CapturingStudioService();
        var reviewerProfile = new ProposedProfileDto(
            "Reviewer", "my-reviewer", "My Reviewer", "Reviews quality.",
            "gpt-4o-mini", "openai", "You review.", null,
            "clarity", null, null, null, null);

        await MaterializeTemplateProposalTool.MaterializeTemplateProposal(
            svc, Guid.NewGuid(), BuildTemplateDto(), [reviewerProfile]);

        Assert.Single(svc.CapturedRequest!.FinalNewProfiles);
        Assert.Equal(ProposedProfileType.Reviewer, svc.CapturedRequest.FinalNewProfiles[0].ProfileType);
        Assert.Equal("my-reviewer", svc.CapturedRequest.FinalNewProfiles[0].Name);
    }

    [Fact]
    public async Task MaterializeTemplateProposal_InvalidProfileType_ThrowsArgumentException()
    {
        var svc = new CapturingStudioService();
        var invalidProfile = new ProposedProfileDto(
            "InvalidType", "name", "Name", "Desc",
            "gpt-4o", "openai", "system prompt", null,
            null, null, null, null, null);

        await Assert.ThrowsAsync<ArgumentException>(
            () => MaterializeTemplateProposalTool.MaterializeTemplateProposal(
                svc, Guid.NewGuid(), BuildTemplateDto(), [invalidProfile]));
    }

    [Fact]
    public async Task MaterializeTemplateProposal_ReturnsCreatedProfileNames()
    {
        var svc = new CapturingStudioService();

        var output = await MaterializeTemplateProposalTool.MaterializeTemplateProposal(
            svc, Guid.NewGuid(), BuildTemplateDto(), []);

        Assert.Single(output.CreatedProfileNames);
        Assert.Equal("my-profile", output.CreatedProfileNames[0]);
    }

    [Fact]
    public async Task MaterializeTemplateProposal_ReturnsWarnings()
    {
        var svc = new CapturingStudioService();

        var output = await MaterializeTemplateProposalTool.MaterializeTemplateProposal(
            svc, Guid.NewGuid(), BuildTemplateDto(), []);

        Assert.Empty(output.Warnings);
    }
}
