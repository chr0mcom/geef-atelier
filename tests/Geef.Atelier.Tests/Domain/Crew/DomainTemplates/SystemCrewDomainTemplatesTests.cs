using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Tests.Domain.Crew.DomainTemplates;

public sealed class SystemCrewDomainTemplatesTests
{
    // ── Juristisch template ──────────────────────────────────────────────────────

    [Fact]
    public void JuristischTemplate_HasCorrectName()
    {
        Assert.Equal("juristisch", SystemCrew.JuristischTemplate.Name);
    }

    [Fact]
    public void JuristischTemplate_IsSystem_UsesDefaultExecutor()
    {
        Assert.True(SystemCrew.JuristischTemplate.IsSystem);
        Assert.Equal(SystemCrew.DefaultExecutorProfile.Name, SystemCrew.JuristischTemplate.ExecutorProfileName);
    }

    [Fact]
    public void JuristischTemplate_HasThreeReviewers_WithGenericReviewers()
    {
        var reviewers = SystemCrew.JuristischTemplate.ReviewerProfileNames;
        Assert.Equal(3, reviewers.Count);
        Assert.Equal("briefing-fidelity", reviewers[0]);
        Assert.Equal("domain-terminology-reviewer", reviewers[1]);
        Assert.Equal("substantive-rigor-reviewer", reviewers[2]);
    }

    [Fact]
    public void JuristischTemplate_BindsLegalPacksToGenericReviewers()
    {
        var b = SystemCrew.JuristischTemplate.ActorPackBindings;
        Assert.Equal(new[] { "legal-terminology" }, b["reviewer:domain-terminology-reviewer"]);
        Assert.Equal(new[] { "legal-clause-risk" }, b["reviewer:substantive-rigor-reviewer"]);
    }

    [Fact]
    public void JuristischTemplate_UsesSequentialStrategy()
    {
        Assert.Equal(EvaluationStrategy.Sequential, SystemCrew.JuristischTemplate.EvaluationStrategy);
    }

    [Fact]
    public void JuristischTemplate_HasLegalDomainExpertAdvisor()
    {
        Assert.Contains("legal-domain-expert", SystemCrew.JuristischTemplate.AdvisorProfileNames);
        Assert.Single(SystemCrew.JuristischTemplate.AdvisorProfileNames);
    }

    [Fact]
    public void JuristischTemplate_HasDefaultGroundingProviders()
    {
        Assert.Equal(3, SystemCrew.JuristischTemplate.GroundingProviderNames.Count);
        Assert.Contains("tavily-refined", SystemCrew.JuristischTemplate.GroundingProviderNames);
        Assert.Contains("run-attachments", SystemCrew.JuristischTemplate.GroundingProviderNames);
        Assert.Contains("learning-retriever-default", SystemCrew.JuristischTemplate.GroundingProviderNames);
    }

    // ── Akademisch template ──────────────────────────────────────────────────────

    [Fact]
    public void AkademischTemplate_HasCorrectName()
    {
        Assert.Equal("akademisch", SystemCrew.AkademischTemplate.Name);
    }

    [Fact]
    public void AkademischTemplate_IsSystem_UsesDefaultExecutor()
    {
        Assert.True(SystemCrew.AkademischTemplate.IsSystem);
        Assert.Equal(SystemCrew.DefaultExecutorProfile.Name, SystemCrew.AkademischTemplate.ExecutorProfileName);
    }

    [Fact]
    public void AkademischTemplate_HasThreeReviewers_WithGenericReviewers()
    {
        var reviewers = SystemCrew.AkademischTemplate.ReviewerProfileNames;
        Assert.Equal(3, reviewers.Count);
        Assert.Equal("briefing-fidelity", reviewers[0]);
        Assert.Equal("domain-terminology-reviewer", reviewers[1]);
        Assert.Equal("substantive-rigor-reviewer", reviewers[2]);
    }

    [Fact]
    public void AkademischTemplate_BindsAcademicPacksToGenericReviewers()
    {
        var b = SystemCrew.AkademischTemplate.ActorPackBindings;
        Assert.Equal(new[] { "academic-citation" }, b["reviewer:domain-terminology-reviewer"]);
        Assert.Equal(new[] { "academic-argumentation" }, b["reviewer:substantive-rigor-reviewer"]);
    }

    [Fact]
    public void AkademischTemplate_UsesSequentialStrategy()
    {
        Assert.Equal(EvaluationStrategy.Sequential, SystemCrew.AkademischTemplate.EvaluationStrategy);
    }

    [Fact]
    public void AkademischTemplate_HasAcademicRigorAdvisor()
    {
        Assert.Contains("academic-rigor-advisor", SystemCrew.AkademischTemplate.AdvisorProfileNames);
        Assert.Single(SystemCrew.AkademischTemplate.AdvisorProfileNames);
    }

    [Fact]
    public void AkademischTemplate_HasDefaultGroundingProviders()
    {
        Assert.Equal(3, SystemCrew.AkademischTemplate.GroundingProviderNames.Count);
        Assert.Contains("tavily-refined", SystemCrew.AkademischTemplate.GroundingProviderNames);
        Assert.Contains("run-attachments", SystemCrew.AkademischTemplate.GroundingProviderNames);
        Assert.Contains("learning-retriever-default", SystemCrew.AkademischTemplate.GroundingProviderNames);
    }

    // ── Marketing template ───────────────────────────────────────────────────────

    [Fact]
    public void MarketingTemplate_HasCorrectName()
    {
        Assert.Equal("marketing", SystemCrew.MarketingTemplate.Name);
    }

    [Fact]
    public void MarketingTemplate_IsSystem_UsesDefaultExecutor()
    {
        Assert.True(SystemCrew.MarketingTemplate.IsSystem);
        Assert.Equal(SystemCrew.DefaultExecutorProfile.Name, SystemCrew.MarketingTemplate.ExecutorProfileName);
    }

    [Fact]
    public void MarketingTemplate_HasThreeReviewers_WithGenericReviewers()
    {
        var reviewers = SystemCrew.MarketingTemplate.ReviewerProfileNames;
        Assert.Equal(3, reviewers.Count);
        Assert.Equal("briefing-fidelity", reviewers[0]);
        Assert.Equal("domain-terminology-reviewer", reviewers[1]);
        Assert.Equal("substantive-rigor-reviewer", reviewers[2]);
    }

    [Fact]
    public void MarketingTemplate_BindsMarketingPacksToGenericReviewers()
    {
        var b = SystemCrew.MarketingTemplate.ActorPackBindings;
        Assert.Equal(new[] { "marketing-voice" }, b["reviewer:domain-terminology-reviewer"]);
        Assert.Equal(new[] { "marketing-conversion" }, b["reviewer:substantive-rigor-reviewer"]);
    }

    [Fact]
    public void MarketingTemplate_UsesParallelStrategy()
    {
        Assert.Equal(EvaluationStrategy.Parallel, SystemCrew.MarketingTemplate.EvaluationStrategy);
    }

    [Fact]
    public void MarketingTemplate_HasNoAdvisors()
    {
        // Deliberate: marketing copy benefits from fast iteration without critical advisory delay
        Assert.Empty(SystemCrew.MarketingTemplate.AdvisorProfileNames);
    }

    [Fact]
    public void MarketingTemplate_HasDefaultGroundingProviders()
    {
        Assert.Equal(3, SystemCrew.MarketingTemplate.GroundingProviderNames.Count);
        Assert.Contains("tavily-refined", SystemCrew.MarketingTemplate.GroundingProviderNames);
        Assert.Contains("run-attachments", SystemCrew.MarketingTemplate.GroundingProviderNames);
        Assert.Contains("learning-retriever-default", SystemCrew.MarketingTemplate.GroundingProviderNames);
    }

    // ── Dictionary completeness ──────────────────────────────────────────────────

    [Fact]
    public void CrewTemplates_ContainsAllFourSystemTemplates()
    {
        Assert.True(SystemCrew.CrewTemplates.ContainsKey("klassik"));
        Assert.True(SystemCrew.CrewTemplates.ContainsKey("juristisch"));
        Assert.True(SystemCrew.CrewTemplates.ContainsKey("akademisch"));
        Assert.True(SystemCrew.CrewTemplates.ContainsKey("marketing"));
    }

    [Fact]
    public void CrewTemplates_HasFourTotalEntries()
    {
        // klassik + juristisch + akademisch + marketing + crew-composer
        Assert.Equal(5, SystemCrew.CrewTemplates.Count);
    }

    [Fact]
    public void IsSystemName_RecognizesAllDomainTemplates()
    {
        Assert.True(SystemCrew.IsSystemName("juristisch"));
        Assert.True(SystemCrew.IsSystemName("akademisch"));
        Assert.True(SystemCrew.IsSystemName("marketing"));
    }

    [Fact]
    public void IsSystemName_RecognizesGenericDomainReviewers()
    {
        Assert.True(SystemCrew.IsSystemName("domain-terminology-reviewer"));
        Assert.True(SystemCrew.IsSystemName("substantive-rigor-reviewer"));
    }
}
