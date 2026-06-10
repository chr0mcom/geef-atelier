using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Tests.Core;

public sealed class SystemCrewToolBindingReviewerTests
{
    [Fact]
    public void CrewComposerToolBindingProfile_IsSystem()
    {
        Assert.True(SystemCrew.CrewComposerToolBindingProfile.IsSystem);
    }

    [Fact]
    public void CrewComposerToolBindingProfile_HasCorrectProviderAndModel()
    {
        Assert.Equal("openrouter", SystemCrew.CrewComposerToolBindingProfile.Provider);
        Assert.StartsWith("google/", SystemCrew.CrewComposerToolBindingProfile.Model);
    }

    [Fact]
    public void CrewComposerTemplate_IncludesToolBindingReviewer()
    {
        Assert.Contains("crew-composer-tool-binding", SystemCrew.CrewComposerTemplate.ReviewerProfileNames);
    }

    [Fact]
    public void SystemReviewerProfiles_ContainsToolBindingReviewer()
    {
        Assert.True(SystemCrew.ReviewerProfiles.ContainsKey("crew-composer-tool-binding"));
    }

    [Fact]
    public void CrewComposerToolBinding_PromptContainsMutating()
    {
        Assert.Contains("Mutating", SystemPrompts.CrewComposerToolBinding);
    }

    [Fact]
    public void CrewComposerToolBinding_PromptContainsStaticContext()
    {
        Assert.Contains("static-context", SystemPrompts.CrewComposerToolBinding);
    }
}
