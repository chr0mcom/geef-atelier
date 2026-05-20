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
    public void TavilyBasicProfile_HasMaxQueriesPerRunOf1()
    {
        Assert.Equal(1, SystemCrew.TavilyBasicProfile.MaxQueriesPerRun);
    }

    [Fact]
    public void GroundingProviderProfiles_ContainsTavilyBasic()
    {
        Assert.True(SystemCrew.GroundingProviderProfiles.ContainsKey("tavily-basic"));
    }

    [Fact]
    public void KlassikTemplate_HasTavilyBasicGroundingProvider()
    {
        Assert.Contains("tavily-basic", SystemCrew.KlassikTemplate.GroundingProviderNames);
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

    [Fact]
    public void TavilyNewsProfile_IsSystem()
    {
        Assert.True(SystemCrew.TavilyNewsProfile.IsSystem);
    }

    [Fact]
    public void TavilyNewsProfile_HasCorrectProviderType()
    {
        Assert.Equal("news-search", SystemCrew.TavilyNewsProfile.ProviderType);
    }

    [Fact]
    public void TavilyNewsProfile_HasExpectedSettings()
    {
        var settings = SystemCrew.TavilyNewsProfile.ProviderSettings;
        Assert.Equal("7", settings[GroundingProviderProfile.KeyRecencyDays]);
        Assert.Equal("5", settings[GroundingProviderProfile.KeyNewsMaxResults]);
    }

    [Fact]
    public void GroundingProviderProfiles_ContainsTavilyNews()
    {
        Assert.True(SystemCrew.GroundingProviderProfiles.ContainsKey("tavily-news"));
    }
}
