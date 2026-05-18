using Bunit;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.TemplateStudio;
using Geef.Atelier.Application.Crew.Knowledge;
using Microsoft.AspNetCore.Components;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.Crew.TemplateStudio;
using Geef.Atelier.Web.Components.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class StudioEditStepTests : TestContext
{
    public StudioEditStepTests()
    {
        Services.AddSingleton<ICrewService>(new EmptyCrewService());
        Services.AddSingleton<IProviderCatalog>(new EmptyProviderCatalog());
        Services.AddSingleton<IModelCatalog>(new EmptyModelCatalog());
        Services.AddSingleton(Options.Create(new TemplateStudioOptions()));
    }

    // -------------------------------------------------------------------------
    // Test doubles
    // -------------------------------------------------------------------------

    private sealed class EmptyCrewService : ICrewService
    {
        public Task<IReadOnlyList<ReviewerProfile>> ListReviewerProfilesAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<ReviewerProfile>)[]);
        public Task<IReadOnlyList<AdvisorProfile>> ListAdvisorProfilesAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<AdvisorProfile>)[]);
        public Task<IReadOnlyList<GroundingProviderProfile>> ListGroundingProviderProfilesAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<GroundingProviderProfile>)[]);
        public Task<IReadOnlyList<ExecutorProfile>> ListExecutorProfilesAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<ExecutorProfile>)[]);
        public Task<IReadOnlyList<CrewTemplate>> ListCrewTemplatesAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<CrewTemplate>)[]);
        public Task<ReviewerProfile?> GetReviewerProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<ReviewerProfile?>(null);
        public Task<ExecutorProfile?> GetExecutorProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<ExecutorProfile?>(null);
        public Task<AdvisorProfile?> GetAdvisorProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<AdvisorProfile?>(null);
        public Task<GroundingProviderProfile?> GetGroundingProviderProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<GroundingProviderProfile?>(null);
        public Task<CrewTemplate?> GetCrewTemplateAsync(string name, CancellationToken ct = default) => Task.FromResult<CrewTemplate?>(null);
        public Task<ReviewerProfile> CreateCustomReviewerProfileAsync(ReviewerProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task<ReviewerProfile> UpdateCustomReviewerProfileAsync(ReviewerProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task DeleteCustomReviewerProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<ExecutorProfile> CreateCustomExecutorProfileAsync(ExecutorProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task<ExecutorProfile> UpdateCustomExecutorProfileAsync(ExecutorProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task DeleteCustomExecutorProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<AdvisorProfile> CreateCustomAdvisorProfileAsync(AdvisorProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task<AdvisorProfile> UpdateCustomAdvisorProfileAsync(AdvisorProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task DeleteCustomAdvisorProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<GroundingProviderProfile> CreateCustomGroundingProviderProfileAsync(GroundingProviderProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task<GroundingProviderProfile> UpdateCustomGroundingProviderProfileAsync(GroundingProviderProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task DeleteCustomGroundingProviderProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<CrewTemplate> CreateCustomCrewTemplateAsync(CrewTemplate template, CancellationToken ct = default) => Task.FromResult(template);
        public Task<CrewTemplate> UpdateCustomCrewTemplateAsync(CrewTemplate template, CancellationToken ct = default) => Task.FromResult(template);
        public Task DeleteCustomCrewTemplateAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomReviewerProfileAsync(string oldName, string newName, CancellationToken ct = default) => Task.FromResult(newName);
        public Task<string> RenameCustomExecutorProfileAsync(string oldName, string newName, CancellationToken ct = default) => Task.FromResult(newName);
        public Task<string> RenameCustomAdvisorProfileAsync(string oldName, string newName, CancellationToken ct = default) => Task.FromResult(newName);
        public Task<string> RenameCustomGroundingProviderProfileAsync(string oldName, string newName, CancellationToken ct = default) => Task.FromResult(newName);
        public Task<string> RenameCustomCrewTemplateAsync(string oldName, string newName, CancellationToken ct = default) => Task.FromResult(newName);
        public Task<CrewSnapshot> ResolveSnapshotAsync(string? crewTemplateName, CrewSpec? customCrew, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class EmptyProviderCatalog : IProviderCatalog
    {
        public IReadOnlyList<ProviderInfo> ListProviders() => [];
    }

    private sealed class EmptyModelCatalog : IModelCatalog
    {
        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(string providerName, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<ModelInfo>)[]);
        public Task<IReadOnlyList<ModelInfo>> RefreshAsync(string providerName, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<ModelInfo>)[]);
        public bool IsUsingFallback(string providerName) => false;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static TemplateStudioAnalysis MakeAnalysis(
        string displayName = "My New Template",
        IReadOnlyList<ProposedProfile>? newProfiles = null) => new(
        Id: Guid.NewGuid(),
        TaskDescription: "Test task.",
        MatchedExistingTemplates: [],
        Recommendation: StudioRecommendation.CreateNewTemplate,
        ProposedTemplate: MakeTemplate(displayName, newProfiles),
        ProposedNewProfiles: newProfiles ?? [],
        ReasoningSummary: "Needs a new template.",
        InputTokens: 100,
        OutputTokens: 50,
        CostEur: null,
        CreatedAt: DateTimeOffset.UtcNow);

    private static ProposedTemplate MakeTemplate(
        string displayName = "My New Template",
        IReadOnlyList<ProposedProfile>? newProfiles = null) => new(
        Name: "my-new-template",
        DisplayName: displayName,
        Description: "A template for tests.",
        ExecutorProfileName: "default-executor",
        ReviewerProfileNames: newProfiles?.Where(p => p.ProfileType == ProposedProfileType.Reviewer).Select(p => p.Name).ToList() ?? ["briefing-fidelity"],
        AdvisorProfileNames: [],
        GroundingProviderProfileNames: [],
        EvaluationStrategy: "Sequential");

    private static ProposedProfile MakeReviewerProfile(string name = "custom-quality-reviewer") => new(
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

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void StudioEditStep_RendersTemplateName()
    {
        var cut = RenderComponent<StudioEditStep>(p =>
            p.Add(c => c.Analysis, MakeAnalysis("My New Template")));

        cut.Find("[data-testid='studio-edit-step']");
        var input = cut.Find("[data-testid='template-display-name']");
        Assert.Equal("My New Template", input.GetAttribute("value"));
    }

    [Fact]
    public void StudioEditStep_RendersSaveAndBackButtons()
    {
        var cut = RenderComponent<StudioEditStep>(p =>
            p.Add(c => c.Analysis, MakeAnalysis()));

        cut.Find("[data-testid='save-button']");
        cut.Find("[data-testid='back-button']");
    }

    [Fact]
    public async Task StudioEditStep_ClickSave_WithCompleteReviewerProfile_FiresOnSave()
    {
        var profile = MakeReviewerProfile("custom-quality-reviewer");
        var analysis = MakeAnalysis(newProfiles: [profile]);
        MaterializationRequest? captured = null;

        var cut = RenderComponent<StudioEditStep>(p =>
        {
            p.Add(c => c.Analysis, analysis);
            p.Add(c => c.OnSave, EventCallback.Factory.Create<MaterializationRequest>(this, r => captured = r));
        });

        await cut.Find("[data-testid='save-button']").ClickAsync(new());

        // Profile has all required fields (from MakeReviewerProfile), so OnSave should fire
        Assert.NotNull(captured);
        Assert.Single(captured!.FinalNewProfiles);
        Assert.Equal("custom-quality-reviewer", captured.FinalTemplate.ReviewerProfileNames[0]);
    }

    [Fact]
    public void StudioEditStep_RendersEvaluationStrategyDropdown()
    {
        var cut = RenderComponent<StudioEditStep>(p =>
            p.Add(c => c.Analysis, MakeAnalysis()));

        var select = cut.Find("[data-testid='template-strategy']");
        Assert.NotNull(select);
    }

    [Fact]
    public void StudioEditStep_AddReviewerButton_AddsNewSlot()
    {
        var cut = RenderComponent<StudioEditStep>(p =>
            p.Add(c => c.Analysis, MakeAnalysis()));

        var beforeCount = cut.FindAll("[data-testid^='reviewer-slot-row-']").Count;
        cut.Find("[data-testid='add-reviewer-btn']").Click();
        var afterCount = cut.FindAll("[data-testid^='reviewer-slot-row-']").Count;

        Assert.Equal(beforeCount + 1, afterCount);
    }
}
