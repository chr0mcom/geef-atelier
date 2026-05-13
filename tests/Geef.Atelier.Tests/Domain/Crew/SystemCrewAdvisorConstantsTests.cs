using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;

namespace Geef.Atelier.Tests.Domain.Crew;

public sealed class SystemCrewAdvisorConstantsTests
{
    [Fact]
    public void BriefingClarifierProfile_HasCorrectName()
    {
        Assert.Equal("briefing-clarifier", SystemCrew.BriefingClarifierProfile.Name);
    }

    [Fact]
    public void DevilsAdvocateProfile_HasCorrectName()
    {
        Assert.Equal("devils-advocate", SystemCrew.DevilsAdvocateProfile.Name);
    }

    [Fact]
    public void BriefingClarifierProfile_HasStrategicMode()
    {
        Assert.Equal(AdvisorMode.Strategic, SystemCrew.BriefingClarifierProfile.Mode);
    }

    [Fact]
    public void DevilsAdvocateProfile_HasDevilsAdvocateModeAndBeforeEveryTrigger()
    {
        Assert.Equal(AdvisorMode.DevilsAdvocate, SystemCrew.DevilsAdvocateProfile.Mode);
        Assert.Equal(AdvisorTrigger.BeforeEveryExecution, SystemCrew.DevilsAdvocateProfile.Trigger);
    }

    [Fact]
    public void AdvisorProfiles_ContainsBothSystemProfiles()
    {
        Assert.True(SystemCrew.AdvisorProfiles.ContainsKey(SystemCrew.BriefingClarifierProfile.Name));
        Assert.True(SystemCrew.AdvisorProfiles.ContainsKey(SystemCrew.DevilsAdvocateProfile.Name));
        Assert.Equal(2, SystemCrew.AdvisorProfiles.Count);
    }

    [Fact]
    public void IsSystemAdvisorName_ReturnsTrue_ForKnownSystemNames()
    {
        Assert.True(SystemCrew.IsSystemAdvisorName(SystemCrew.BriefingClarifierProfile.Name));
        Assert.True(SystemCrew.IsSystemAdvisorName(SystemCrew.DevilsAdvocateProfile.Name));
    }

    [Fact]
    public void IsSystemAdvisorName_ReturnsFalse_ForCustomPrefix()
    {
        Assert.False(SystemCrew.IsSystemAdvisorName("custom-my-advisor"));
        Assert.False(SystemCrew.IsSystemAdvisorName("unknown-advisor"));
        Assert.False(SystemCrew.IsSystemAdvisorName("briefing-fidelity")); // reviewer, not advisor
    }
}
