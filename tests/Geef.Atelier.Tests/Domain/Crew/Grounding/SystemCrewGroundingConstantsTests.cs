using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Grounding;

namespace Geef.Atelier.Tests.Domain.Crew.Grounding;

public sealed class SystemCrewGroundingConstantsTests
{
    [Fact]
    public void TavilyBasicProfile_IsSystem()
    {
        Assert.True(SystemCrew.TavilyBasicProfile.IsSystem);
    }

    [Fact]
    public void TavilyBasicProfile_HasCorrectProviderType()
    {
        Assert.Equal("tavily", SystemCrew.TavilyBasicProfile.ProviderType);
    }

    [Fact]
    public void TavilyBasicProfile_HasExpectedSettings()
    {
        var settings = SystemCrew.TavilyBasicProfile.ProviderSettings;
        Assert.Equal("basic", settings["Tier"]);
        Assert.Equal("5", settings["MaxResults"]);
        Assert.Equal("true", settings["IncludeAnswer"]);
        Assert.Equal("0.4", settings["MinRelevanceScore"]);
        Assert.Equal("true", settings["ExtractQuery"]);
    }

    [Fact]
    public void GroundingProviderProfiles_ContainsTavilyBasic()
    {
        Assert.True(SystemCrew.GroundingProviderProfiles.ContainsKey("tavily-basic"));
    }

    [Fact]
    public void KlassikTemplate_HasTavilyRefinedGroundingProvider()
    {
        Assert.Contains("tavily-refined", SystemCrew.KlassikTemplate.GroundingProviderNames);
    }

    [Fact]
    public void IsSystemGroundingProviderName_ReturnsTrueForTavilyBasic()
    {
        Assert.True(SystemCrew.IsSystemGroundingProviderName("tavily-basic"));
    }

    [Fact]
    public void IsSystemGroundingProviderName_ReturnsFalseForCustom()
    {
        Assert.False(SystemCrew.IsSystemGroundingProviderName("custom-my-provider"));
    }

    [Theory]
    [InlineData("tavily-news")]
    [InlineData("academic-default")]
    public void The_system_profiles_of_retired_types_are_gone(string name)
    {
        Assert.False(SystemCrew.GroundingProviderProfiles.ContainsKey(name));
    }

    [Fact]
    public void No_system_profile_uses_a_retired_type()
    {
        Assert.DoesNotContain(SystemCrew.GroundingProviderProfiles.Values, p => p.ProviderType.IsRetiredGroundingType());
    }
}
