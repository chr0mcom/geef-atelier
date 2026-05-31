using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;

namespace Geef.Atelier.Tests.Domain.Crew.DomainTemplates;

public sealed class SystemCrewDomainAdvisorsTests
{
    [Fact]
    public void LegalDomainExpert_HasCorrectName()
    {
        Assert.Equal("legal-domain-expert", SystemCrew.LegalDomainExpertProfile.Name);
    }

    [Fact]
    public void LegalDomainExpert_IsDomainExpertMode_BeforeFirstExecution()
    {
        Assert.Equal(AdvisorMode.DomainExpert, SystemCrew.LegalDomainExpertProfile.Mode);
        Assert.Equal(AdvisorTrigger.BeforeFirstExecution, SystemCrew.LegalDomainExpertProfile.Trigger);
    }

    [Fact]
    public void LegalDomainExpert_IsSystem_UsesClaudioCli_Opus47()
    {
        Assert.True(SystemCrew.LegalDomainExpertProfile.IsSystem);
        Assert.Equal("claude-cli", SystemCrew.LegalDomainExpertProfile.Provider);
        Assert.Equal("claude-opus-4-8", SystemCrew.LegalDomainExpertProfile.Model);
    }

    [Fact]
    public void AcademicRigorAdvisor_HasCorrectName()
    {
        Assert.Equal("academic-rigor-advisor", SystemCrew.AcademicRigorAdvisorProfile.Name);
    }

    [Fact]
    public void AcademicRigorAdvisor_IsCriticalMode_BeforeEveryExecution()
    {
        Assert.Equal(AdvisorMode.Critical, SystemCrew.AcademicRigorAdvisorProfile.Mode);
        Assert.Equal(AdvisorTrigger.BeforeEveryExecution, SystemCrew.AcademicRigorAdvisorProfile.Trigger);
    }

    [Fact]
    public void AcademicRigorAdvisor_IsSystem_UsesClaudioCli_Opus47()
    {
        Assert.True(SystemCrew.AcademicRigorAdvisorProfile.IsSystem);
        Assert.Equal("claude-cli", SystemCrew.AcademicRigorAdvisorProfile.Provider);
        Assert.Equal("claude-opus-4-8", SystemCrew.AcademicRigorAdvisorProfile.Model);
    }

    [Fact]
    public void AdvisorProfiles_ContainsBothDomainAdvisors()
    {
        Assert.True(SystemCrew.AdvisorProfiles.ContainsKey("legal-domain-expert"));
        Assert.True(SystemCrew.AdvisorProfiles.ContainsKey("academic-rigor-advisor"));
    }

    [Fact]
    public void AdvisorProfiles_HasFourTotalEntries()
    {
        // 2 original (briefing-clarifier, devils-advocate) + 2 domain-specific
        Assert.Equal(4, SystemCrew.AdvisorProfiles.Count);
    }

    [Fact]
    public void IsSystemAdvisorName_RecognizesDomainAdvisors()
    {
        Assert.True(SystemCrew.IsSystemAdvisorName("legal-domain-expert"));
        Assert.True(SystemCrew.IsSystemAdvisorName("academic-rigor-advisor"));
    }

    [Fact]
    public void LegalDomainExpert_SystemPrompt_MentionsLegalConstraints()
    {
        Assert.Contains("Legal constraints", SystemCrew.LegalDomainExpertProfile.SystemPrompt,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AcademicRigorAdvisor_SystemPrompt_MentionsIterationVariance()
    {
        Assert.Contains("iteration", SystemCrew.AcademicRigorAdvisorProfile.SystemPrompt,
            StringComparison.OrdinalIgnoreCase);
    }
}
