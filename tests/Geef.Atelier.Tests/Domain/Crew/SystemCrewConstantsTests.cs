using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Tests.Domain.Crew;

/// <summary>Since stage 8d (D-091) the only setups are the two code-only templates of the direct path.</summary>
public sealed class SystemCrewConstantsTests
{
    [Fact]
    public void AllCodeOnlyProfiles_HaveIsSystemTrue()
    {
        Assert.All(SystemCrew.CodeOnlyExecutorProfiles.Values, p => Assert.True(p.IsSystem, p.Name));
        Assert.All(SystemCrew.CodeOnlyReviewerProfiles.Values, p => Assert.True(p.IsSystem, p.Name));
    }

    [Fact]
    public void TheOnlyTemplatesAreTheDirectAndTheOutlineTemplate()
    {
        Assert.Equal([SystemCrew.DirectTemplateName, SystemCrew.DirectOutlineTemplateName], SystemCrew.CodeOnlyTemplates.Keys.Order().ToList());
        Assert.All(SystemCrew.CodeOnlyTemplates.Keys, name => Assert.True(SystemCrew.IsDirectPathTemplate(name)));
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

    [Fact]
    public void TheOutlineTemplateAddsOnlyTheRequestCheck()
    {
        Assert.Equal(SystemCrew.DirectTemplate.ExecutorProfileName, SystemCrew.DirectOutlineTemplate.ExecutorProfileName);
        Assert.Equal([.. SystemCrew.DirectTemplate.ReviewerProfileNames, SystemCrew.RequirementsCheckProfile.Name],
            SystemCrew.DirectOutlineTemplate.ReviewerProfileNames);
        Assert.Equal(SystemCrew.DirectTemplate.ConvergenceOverride, SystemCrew.DirectOutlineTemplate.ConvergenceOverride);
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
