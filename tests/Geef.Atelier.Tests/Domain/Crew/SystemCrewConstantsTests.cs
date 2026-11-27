using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Tests.Domain.Crew;

/// <summary>The only setup is the code-only template of the direct path (D-091, addendum to D-087).</summary>
public sealed class SystemCrewConstantsTests
{
    [Fact]
    public void AllCodeOnlyProfiles_HaveIsSystemTrue()
    {
        Assert.All(SystemCrew.CodeOnlyExecutorProfiles.Values, p => Assert.True(p.IsSystem, p.Name));
        Assert.All(SystemCrew.CodeOnlyReviewerProfiles.Values, p => Assert.True(p.IsSystem, p.Name));
    }

    [Fact]
    public void TheOnlyTemplateIsTheDirectTemplate()
    {
        Assert.Equal([SystemCrew.DirectTemplateName], SystemCrew.CodeOnlyTemplates.Keys.ToList());
        Assert.All(SystemCrew.CodeOnlyTemplates.Keys, name => Assert.True(SystemCrew.IsDirectPathTemplate(name)));
        Assert.DoesNotContain(SystemCrew.RetiredRequestCheckName, SystemCrew.CodeOnlyReviewerProfiles.Keys);
    }

    [Fact]
    public void StoredRunsOfTheRemovedOutlineVariantStillCountAsDirectPath()
    {
        Assert.Equal("direct-outline", SystemCrew.RetiredOutlineTemplateName);
        Assert.Equal("requirements-check", SystemCrew.RetiredRequestCheckName);
        Assert.True(SystemCrew.IsDirectPathTemplate(SystemCrew.RetiredOutlineTemplateName));
    }

    [Fact]
    public void EveryTemplateReferencesExistingCodeOnlyProfiles()
    {
        foreach (var template in SystemCrew.CodeOnlyTemplates.Values)
        {
            Assert.True(SystemCrew.CodeOnlyExecutorProfiles.ContainsKey(template.ExecutorProfileName), template.Name);
            Assert.All(template.ReviewerProfileNames, name => Assert.True(SystemCrew.CodeOnlyReviewerProfiles.ContainsKey(name), $"{template.Name}/{name}"));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("klassik")]
    [InlineData("Direct")]
    [InlineData("test-loop")]
    public void OtherNamesAreNoDirectPathTemplate(string? name) =>
        Assert.False(SystemCrew.IsDirectPathTemplate(name));
}
