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
    public void JuristischTemplate_HasThreeReviewers_WithBriefingFidelityFirst()
    {
        var reviewers = SystemCrew.JuristischTemplate.ReviewerProfileNames;
        Assert.Equal(3, reviewers.Count);
        Assert.Equal("briefing-fidelity", reviewers[0]);
        Assert.Equal("legal-jargon-precision", reviewers[1]);
        Assert.Equal("legal-clause-risk", reviewers[2]);
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
    public void JuristischTemplate_HasNoGroundingProviders()
    {
        Assert.Empty(SystemCrew.JuristischTemplate.GroundingProviderNames);
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
    public void AkademischTemplate_HasThreeReviewers_WithBriefingFidelityFirst()
    {
        var reviewers = SystemCrew.AkademischTemplate.ReviewerProfileNames;
        Assert.Equal(3, reviewers.Count);
        Assert.Equal("briefing-fidelity", reviewers[0]);
        Assert.Equal("academic-citation-readiness", reviewers[1]);
        Assert.Equal("academic-argumentation-rigor", reviewers[2]);
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
    public void AkademischTemplate_HasNoGroundingProviders()
    {
        Assert.Empty(SystemCrew.AkademischTemplate.GroundingProviderNames);
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
    public void MarketingTemplate_HasThreeReviewers_WithBriefingFidelityFirst()
    {
        var reviewers = SystemCrew.MarketingTemplate.ReviewerProfileNames;
        Assert.Equal(3, reviewers.Count);
        Assert.Equal("briefing-fidelity", reviewers[0]);
        Assert.Equal("marketing-audience-clarity", reviewers[1]);
        Assert.Equal("marketing-conversion-strength", reviewers[2]);
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
    public void MarketingTemplate_HasNoGroundingProviders()
    {
        Assert.Empty(SystemCrew.MarketingTemplate.GroundingProviderNames);
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
        Assert.Equal(4, SystemCrew.CrewTemplates.Count);
    }

    [Fact]
    public void IsSystemName_RecognizesAllDomainTemplates()
    {
        Assert.True(SystemCrew.IsSystemName("juristisch"));
        Assert.True(SystemCrew.IsSystemName("akademisch"));
        Assert.True(SystemCrew.IsSystemName("marketing"));
    }

    [Fact]
    public void IsSystemName_RecognizesAllDomainReviewers()
    {
        Assert.True(SystemCrew.IsSystemName("legal-jargon-precision"));
        Assert.True(SystemCrew.IsSystemName("legal-clause-risk"));
        Assert.True(SystemCrew.IsSystemName("academic-citation-readiness"));
        Assert.True(SystemCrew.IsSystemName("academic-argumentation-rigor"));
        Assert.True(SystemCrew.IsSystemName("marketing-audience-clarity"));
        Assert.True(SystemCrew.IsSystemName("marketing-conversion-strength"));
    }
}
