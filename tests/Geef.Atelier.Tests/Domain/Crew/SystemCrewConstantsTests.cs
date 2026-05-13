using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Tests.Domain.Crew;

public sealed class SystemCrewConstantsTests
{
    [Fact]
    public void AllSystemProfiles_HaveIsSystemTrue()
    {
        Assert.True(SystemCrew.DefaultExecutorProfile.IsSystem);
        Assert.True(SystemCrew.BriefingFidelityProfile.IsSystem);
        Assert.True(SystemCrew.ClarityProfile.IsSystem);
    }

    [Fact]
    public void KlassikTemplate_HasIsSystemTrue()
    {
        Assert.True(SystemCrew.KlassikTemplate.IsSystem);
    }

    [Fact]
    public void KlassikTemplate_ReferencesExistingSystemProfiles()
    {
        var reviewerNames = SystemCrew.KlassikTemplate.ReviewerProfileNames;
        Assert.Contains(SystemCrew.BriefingFidelityProfile.Name, reviewerNames);
        Assert.Contains(SystemCrew.ClarityProfile.Name, reviewerNames);
        Assert.Equal(SystemCrew.DefaultExecutorProfile.Name, SystemCrew.KlassikTemplate.ExecutorProfileName);
    }

    [Fact]
    public void ReviewerProfiles_DictContainsAllSystemReviewers()
    {
        Assert.True(SystemCrew.ReviewerProfiles.ContainsKey(SystemCrew.BriefingFidelityProfile.Name));
        Assert.True(SystemCrew.ReviewerProfiles.ContainsKey(SystemCrew.ClarityProfile.Name));
    }

    [Fact]
    public void ExecutorProfiles_DictContainsDefaultExecutor()
    {
        Assert.True(SystemCrew.ExecutorProfiles.ContainsKey(SystemCrew.DefaultExecutorProfile.Name));
    }

    [Fact]
    public void CrewTemplates_DictContainsKlassik()
    {
        Assert.True(SystemCrew.CrewTemplates.ContainsKey(SystemCrew.KlassikTemplateName));
    }

    [Fact]
    public void IsSystemName_RecognizesAllSystemNames()
    {
        Assert.True(SystemCrew.IsSystemName(SystemCrew.DefaultExecutorProfile.Name));
        Assert.True(SystemCrew.IsSystemName(SystemCrew.BriefingFidelityProfile.Name));
        Assert.True(SystemCrew.IsSystemName(SystemCrew.ClarityProfile.Name));
        Assert.True(SystemCrew.IsSystemName(SystemCrew.KlassikTemplateName));
    }

    [Fact]
    public void IsSystemName_ReturnsFalseForCustomNames()
    {
        Assert.False(SystemCrew.IsSystemName("custom-my-reviewer"));
        Assert.False(SystemCrew.IsSystemName("unknown-name"));
    }

    [Fact]
    public void EnsureCustomPrefix_AddsPrefixWhenMissing()
    {
        Assert.Equal("custom-my-profile", SystemCrew.EnsureCustomPrefix("my-profile"));
    }

    [Fact]
    public void EnsureCustomPrefix_DoesNotDoublePrefixWhenAlreadyPresent()
    {
        Assert.Equal("custom-my-profile", SystemCrew.EnsureCustomPrefix("custom-my-profile"));
    }
}
