using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.Providers;
using Geef.Atelier.Application.Providers;
using Geef.Atelier.Infrastructure.Grounding;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class GroundingProviderEditorRefinementTests : TestContext
{
    private static GroundingProviderProfile MakeGrounding(
        string name = "custom-test",
        Dictionary<string, string>? settings = null) =>
        new(Name: name,
            DisplayName: name + " Display",
            Description: "desc",
            ProviderType: "tavily",
            ProviderSettings: settings ?? new(),
            MaxQueriesPerRun: 1,
            IsSystem: false);

    private void RegisterServices(GroundingProviderProfile? profile = null)
    {
        Services.AddSingleton<ICrewService>(new StubCrewService(profile));
        Services.AddSingleton<IProviderService>(new FakeProviderService());
        Services.AddSingleton<IOptions<TavilyOptions>>(Options.Create(new TavilyOptions { ApiKey = "test-key" }));
        Services.AddSingleton<IModelCatalog>(new StubModelCatalog());
        this.AddTestAuthorization().SetAuthorized("test-user");
    }

    [Fact]
    public void RefinementSection_WhenDisabled_ProviderSelectNotVisible()
    {
        // No refinement keys in settings → toggle starts unchecked → provider select hidden.
        RegisterServices();

        var cut = RenderComponent<GroundingProviderEditor>();

        Assert.Empty(cut.FindAll("[data-testid='refinement-provider-select']"));
    }

    [Fact]
    public void RefinementSection_WhenDisabled_ModelSelectorNotVisible()
    {
        RegisterServices();

        var cut = RenderComponent<GroundingProviderEditor>();

        Assert.Empty(cut.FindAll("[data-testid='refinement-model-selector']"));
    }

    [Fact]
    public void RefinementSection_WhenEnabled_ProviderSelectVisible()
    {
        RegisterServices();

        var cut = RenderComponent<GroundingProviderEditor>();

        // Two checkboxes in Tavily mode: IncludeAnswer and RefinementEnabled (second one).
        var checkboxes = cut.FindAll("input[type='checkbox']");
        // The refinement toggle is the last checkbox in the form.
        checkboxes[^1].Change(true);

        cut.Find("[data-testid='refinement-provider-select']");
    }

    [Fact]
    public void RefinementSection_WhenEnabled_ModelSelectorVisible()
    {
        RegisterServices();

        var cut = RenderComponent<GroundingProviderEditor>();

        var checkboxes = cut.FindAll("input[type='checkbox']");
        checkboxes[^1].Change(true);

        cut.Find("[data-testid='refinement-model-selector']");
    }

    [Fact]
    public void RefinementSection_WhenEnabled_ModeDropdownVisible()
    {
        RegisterServices();

        var cut = RenderComponent<GroundingProviderEditor>();

        var checkboxes = cut.FindAll("input[type='checkbox']");
        checkboxes[^1].Change(true);

        cut.Find("[data-testid='refinement-mode-select']");
    }

    [Fact]
    public void RefinementSection_ExistingProfileWithRefinement_StartsEnabled()
    {
        // Profile has refinement keys set → toggle starts checked → fields visible.
        var settings = new Dictionary<string, string>
        {
            [GroundingProviderProfile.KeyRefinementProvider] = "openrouter",
            [GroundingProviderProfile.KeyRefinementModel] = "gpt-4o",
        };
        var profile = MakeGrounding("custom-refined", settings);
        RegisterServices(profile);

        var cut = RenderComponent<GroundingProviderEditor>(
            p => p.Add(c => c.Name, "custom-refined"));

        // Refinement fields should already be visible.
        cut.Find("[data-testid='refinement-provider-select']");
        cut.Find("[data-testid='refinement-model-selector']");
    }

    // -------------------------------------------------------------------------
    // Fakes
    // -------------------------------------------------------------------------

    private sealed class StubCrewService(GroundingProviderProfile? profile = null) : ICrewService
    {
        private readonly List<GroundingProviderProfile> _profiles = profile != null ? [profile] : [];

        public Task<IReadOnlyList<GroundingProviderProfile>> ListGroundingProviderProfilesAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GroundingProviderProfile>>(_profiles);
        public Task<GroundingProviderProfile?> GetGroundingProviderProfileAsync(string name, CancellationToken ct = default)
            => Task.FromResult(_profiles.FirstOrDefault(p => p.Name == name));
        public Task<GroundingProviderProfile> CreateCustomGroundingProviderProfileAsync(GroundingProviderProfile p, CancellationToken ct = default)
        { _profiles.Add(p); return Task.FromResult(p); }
        public Task<GroundingProviderProfile> UpdateCustomGroundingProviderProfileAsync(GroundingProviderProfile p, CancellationToken ct = default)
            => Task.FromResult(p);
        public Task DeleteCustomGroundingProviderProfileAsync(string name, CancellationToken ct = default)
        { _profiles.RemoveAll(p => p.Name == name); return Task.CompletedTask; }

        public Task<IReadOnlyList<ReviewerProfile>> ListReviewerProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ReviewerProfile>>([]);
        public Task<ReviewerProfile?> GetReviewerProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<ReviewerProfile?>(null);
        public Task<ReviewerProfile> CreateCustomReviewerProfileAsync(ReviewerProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<ReviewerProfile> UpdateCustomReviewerProfileAsync(ReviewerProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomReviewerProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ExecutorProfile>> ListExecutorProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ExecutorProfile>>([]);
        public Task<ExecutorProfile?> GetExecutorProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<ExecutorProfile?>(null);
        public Task<ExecutorProfile> CreateCustomExecutorProfileAsync(ExecutorProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<ExecutorProfile> UpdateCustomExecutorProfileAsync(ExecutorProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomExecutorProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<AdvisorProfile>> ListAdvisorProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AdvisorProfile>>([]);
        public Task<AdvisorProfile?> GetAdvisorProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<AdvisorProfile?>(null);
        public Task<AdvisorProfile> CreateCustomAdvisorProfileAsync(AdvisorProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<AdvisorProfile> UpdateCustomAdvisorProfileAsync(AdvisorProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomAdvisorProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<CrewTemplate>> ListCrewTemplatesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CrewTemplate>>([]);
        public Task<CrewTemplate?> GetCrewTemplateAsync(string name, CancellationToken ct = default) => Task.FromResult<CrewTemplate?>(null);
        public Task<CrewTemplate> CreateCustomCrewTemplateAsync(CrewTemplate t, CancellationToken ct = default) => Task.FromResult(t);
        public Task<CrewTemplate> UpdateCustomCrewTemplateAsync(CrewTemplate t, CancellationToken ct = default) => Task.FromResult(t);
        public Task DeleteCustomCrewTemplateAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomReviewerProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<string> RenameCustomExecutorProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<string> RenameCustomAdvisorProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<string> RenameCustomGroundingProviderProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<FinalizerProfile>> ListFinalizerProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<FinalizerProfile>>([]);
        public Task<FinalizerProfile?> GetFinalizerProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<FinalizerProfile?>(null);
        public Task<FinalizerProfile> CreateCustomFinalizerProfileAsync(FinalizerProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<FinalizerProfile> UpdateCustomFinalizerProfileAsync(FinalizerProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomFinalizerProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomFinalizerProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<string> RenameCustomCrewTemplateAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<CrewSnapshot> ResolveSnapshotAsync(string? name, CrewSpec? spec, CancellationToken ct = default)
            => Task.FromResult(new CrewSnapshot(1, "klassik", SystemCrew.DefaultExecutorProfile, [], EvaluationStrategy.Parallel, null, []));
    }

    private sealed class StubModelCatalog : IModelCatalog
    {
        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(string providerName, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ModelInfo>>([]);

        public Task<IReadOnlyList<ModelInfo>> RefreshAsync(string providerName, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ModelInfo>>([]);

        public bool IsUsingFallback(string providerName) => false;
    }
}
