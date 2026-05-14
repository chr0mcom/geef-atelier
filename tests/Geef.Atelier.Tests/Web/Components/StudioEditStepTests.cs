using Bunit;
using Geef.Atelier.Core.Domain.Crew.TemplateStudio;
using Geef.Atelier.Web.Components.UI;
using Microsoft.AspNetCore.Components;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class StudioEditStepTests : TestContext
{
    private static TemplateStudioAnalysis MakeAnalysis() => new(
        Id: Guid.NewGuid(),
        TaskDescription: "Test task.",
        MatchedExistingTemplates: [],
        Recommendation: StudioRecommendation.CreateNewTemplate,
        ProposedTemplate: MakeTemplate(),
        ProposedNewProfiles: [],
        ReasoningSummary: "Needs a new template.",
        InputTokens: 100,
        OutputTokens: 50,
        CostEur: null,
        CreatedAt: DateTimeOffset.UtcNow);

    private static ProposedTemplate MakeTemplate(string displayName = "My New Template") => new(
        Name: "my-new-template",
        DisplayName: displayName,
        Description: "A template for tests.",
        ExecutorProfileName: "default-executor",
        ReviewerProfileNames: ["briefing-fidelity"],
        AdvisorProfileNames: [],
        GroundingProviderProfileNames: [],
        EvaluationStrategy: "Sequential");

    private static ProposedProfile MakeProfile(string name = "custom-quality-reviewer") => new(
        ProfileType: ProposedProfileType.Reviewer,
        Name: name,
        DisplayName: "Quality Reviewer",
        Description: "Reviews quality.",
        Model: "gpt-4o-mini",
        Provider: "openrouter",
        SystemPrompt: "Review content quality carefully.",
        MaxTokens: null,
        ReviewerFocus: null,
        AdvisorMode: null,
        AdvisorTrigger: null,
        GroundingProviderType: null,
        GroundingProviderSettings: null);

    [Fact]
    public void StudioEditStep_RendersTemplateName()
    {
        var cut = RenderComponent<StudioEditStep>(p =>
        {
            p.Add(c => c.Analysis, MakeAnalysis());
            p.Add(c => c.EditedTemplate, MakeTemplate("My New Template"));
            p.Add(c => c.EditedProfiles, []);
        });

        cut.Find("[data-testid='studio-edit-step']");
        var input = cut.Find("[data-testid='template-display-name']");
        Assert.Equal("My New Template", input.GetAttribute("value"));
    }

    [Fact]
    public void StudioEditStep_EditingProfilePrompt_UpdatesLocalStateOnly()
    {
        // This test verifies the edit step renders profile prompt fields
        // and that the component tracks local state without triggering service calls
        var profile = MakeProfile();
        var editedProfiles = new List<ProposedProfile> { profile };
        var serviceCallCount = 0;

        var cut = RenderComponent<StudioEditStep>(p =>
        {
            p.Add(c => c.Analysis, MakeAnalysis());
            p.Add(c => c.EditedTemplate, MakeTemplate());
            p.Add(c => c.EditedProfiles, editedProfiles);
            p.Add(c => c.OnSave, EventCallback.Factory.Create(this, () => serviceCallCount++));
        });

        var promptTextarea = cut.Find($"[data-testid='profile-prompt-{profile.Name}']");
        Assert.NotNull(promptTextarea);

        // Editing the textarea doesn't trigger OnSave (no service call)
        promptTextarea.Input("Updated system prompt text.");
        Assert.Equal(0, serviceCallCount);
    }
}
