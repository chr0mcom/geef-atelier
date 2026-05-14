using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Tests.Domain.Crew.Grounding;

public sealed class SystemCrewKnowledgeBaseProfileTests
{
    [Fact]
    public void KnowledgeBaseDefaultProfile_IsSystem()
    {
        Assert.True(SystemCrew.KnowledgeBaseDefaultProfile.IsSystem);
    }

    [Fact]
    public void KnowledgeBaseDefaultProfile_HasCorrectProviderType()
    {
        Assert.Equal("vector-store", SystemCrew.KnowledgeBaseDefaultProfile.ProviderType);
    }

    [Fact]
    public void KnowledgeBaseDefaultProfile_HasTopKOf5()
    {
        Assert.Equal("5", SystemCrew.KnowledgeBaseDefaultProfile.ProviderSettings["TopK"]);
    }

    [Fact]
    public void KnowledgeBaseDefaultProfile_HasMaxQueriesPerRunOf1()
    {
        Assert.Equal(1, SystemCrew.KnowledgeBaseDefaultProfile.MaxQueriesPerRun);
    }

    [Fact]
    public void KnowledgeBaseDefaultProfile_HasExpectedName()
    {
        Assert.Equal("knowledge-base-default", SystemCrew.KnowledgeBaseDefaultProfile.Name);
    }

    [Fact]
    public void GroundingProviderProfiles_ContainsKnowledgeBaseDefault()
    {
        Assert.True(SystemCrew.GroundingProviderProfiles.ContainsKey("knowledge-base-default"));
    }

    [Fact]
    public void IsSystemGroundingProviderName_ReturnsTrueForKnowledgeBaseDefault()
    {
        Assert.True(SystemCrew.IsSystemGroundingProviderName("knowledge-base-default"));
    }

    [Fact]
    public void GroundingProviderProfiles_StillContainsTavilyBasic()
    {
        Assert.True(SystemCrew.GroundingProviderProfiles.ContainsKey("tavily-basic"));
    }

    [Fact]
    public void KnowledgeBaseDefaultProfile_DisplayNameIsNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(SystemCrew.KnowledgeBaseDefaultProfile.DisplayName));
    }

    [Fact]
    public void KnowledgeBaseDefaultProfile_DescriptionIsNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(SystemCrew.KnowledgeBaseDefaultProfile.Description));
    }
}
