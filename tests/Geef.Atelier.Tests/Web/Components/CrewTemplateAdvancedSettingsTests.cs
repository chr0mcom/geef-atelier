using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Web.Components;

/// <summary>
/// Convergence ("Advanced") settings must be visible everywhere: the editor pre-fills the
/// global defaults when a template has no explicit override, the view page always shows the
/// effective values, and an override is only persisted for fields that differ from defaults.
/// </summary>
public sealed class CrewTemplateAdvancedSettingsTests : TestContext
{
    private static readonly ConvergenceOptions Defaults = new()
    {
        MaxIterations = 3,
        AbortOnCritical = false,
        DetectRegression = true,
        StagnationThreshold = 3,
    };

    private static CrewTemplate Template(string name, bool isSystem, ConvergencePolicyOverride? overr) => new(
        Name: name,
        DisplayName: name + " Display",
        Description: "desc",
        ExecutorProfileName: "default-executor",
        ReviewerProfileNames: [],
        EvaluationStrategy: EvaluationStrategy.Parallel,
        ConvergenceOverride: overr,
        AdvisorProfileNames: [],
        GroundingProviderNames: [],
        IsSystem: isSystem);

    private void Wire(StubCrewService stub)
    {
        Services.AddSingleton<ICrewService>(stub);
        Services.AddSingleton<IOptions<ConvergenceOptions>>(Options.Create(Defaults));
        this.AddTestAuthorization().SetAuthorized("test-user");
    }

    [Fact]
    public void ViewPage_SystemTemplateWithoutOverride_ShowsDefaultConvergenceValues()
    {
        Wire(new StubCrewService(Template("klassik", isSystem: true, overr: null)));

        var cut = RenderComponent<CrewTemplateView>(p => p.Add(c => c.Name, "klassik"));

        var maxIter = cut.Find("[data-testid='detail-max-iterations']");
        Assert.Contains("3", maxIter.TextContent);
        var detectReg = cut.Find("[data-testid='detail-detect-regression']");
        Assert.Contains("Yes", detectReg.TextContent);
    }

    [Fact]
    public void ViewPage_SystemTemplateWithOverride_ShowsOverrideValues()
    {
        Wire(new StubCrewService(Template("klassik", isSystem: true,
            overr: new ConvergencePolicyOverride(MaxIterations: 9, AbortOnCritical: null,
                DetectRegression: null, StagnationThreshold: null))));

        var cut = RenderComponent<CrewTemplateView>(p => p.Add(c => c.Name, "klassik"));

        var maxIter = cut.Find("[data-testid='detail-max-iterations']");
        Assert.Contains("9", maxIter.TextContent);
        // Unset override field falls back to the global default.
        var stagnation = cut.Find("[data-testid='detail-stagnation']");
        Assert.Contains("3", stagnation.TextContent);
    }

    [Fact]
    public void Editor_CustomTemplateWithoutOverride_PrefillsAdvancedFieldsWithDefaults()
    {
        Wire(new StubCrewService(Template("custom-klassik-copy", isSystem: false, overr: null)));

        var cut = RenderComponent<CrewTemplateEditor>(p => p.Add(c => c.Name, "custom-klassik-copy"));

        var maxIter = cut.Find("[data-testid='advanced-max-iterations']");
        Assert.Equal("3", maxIter.GetAttribute("value"));
        var detectReg = cut.Find("[data-testid='advanced-detect-regression']");
        Assert.True(detectReg.HasAttribute("checked"));
    }

    [Fact]
    public async Task Editor_SaveWithDefaultsUnchanged_PersistsNullOverride()
    {
        var stub = new StubCrewService(Template("custom-klassik-copy", isSystem: false, overr: null));
        Wire(stub);

        var cut = RenderComponent<CrewTemplateEditor>(p => p.Add(c => c.Name, "custom-klassik-copy"));
        await cut.Find("[data-testid='template-editor-form']").SubmitAsync();

        Assert.NotNull(stub.LastUpdated);
        Assert.Null(stub.LastUpdated!.ConvergenceOverride);
    }

    [Fact]
    public async Task Editor_SaveWithChangedField_PersistsOnlyThatFieldAsOverride()
    {
        var stub = new StubCrewService(Template("custom-klassik-copy", isSystem: false, overr: null));
        Wire(stub);

        var cut = RenderComponent<CrewTemplateEditor>(p => p.Add(c => c.Name, "custom-klassik-copy"));
        cut.Find("[data-testid='advanced-max-iterations']").Change("7");
        await cut.Find("[data-testid='template-editor-form']").SubmitAsync();

        Assert.NotNull(stub.LastUpdated?.ConvergenceOverride);
        var co = stub.LastUpdated!.ConvergenceOverride!;
        Assert.Equal(7, co.MaxIterations);
        Assert.Null(co.AbortOnCritical);
        Assert.Null(co.DetectRegression);
        Assert.Null(co.StagnationThreshold);
    }

    private sealed class StubCrewService(CrewTemplate template) : ICrewService
    {
        public CrewTemplate? LastUpdated { get; private set; }

        public Task<CrewTemplate?> GetCrewTemplateAsync(string name, CancellationToken ct = default)
            => Task.FromResult(template.Name == name ? template : null);
        public Task<CrewTemplate> UpdateCustomCrewTemplateAsync(CrewTemplate t, CancellationToken ct = default)
        { LastUpdated = t; return Task.FromResult(t); }
        public Task<CrewTemplate> CreateCustomCrewTemplateAsync(CrewTemplate t, CancellationToken ct = default)
        { LastUpdated = t; return Task.FromResult(t); }
        public Task<IReadOnlyList<CrewTemplate>> ListCrewTemplatesAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CrewTemplate>>([template]);
        public Task DeleteCustomCrewTemplateAsync(string name, CancellationToken ct = default) => Task.CompletedTask;

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
        public Task<IReadOnlyList<GroundingProviderProfile>> ListGroundingProviderProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GroundingProviderProfile>>([]);
        public Task<GroundingProviderProfile?> GetGroundingProviderProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<GroundingProviderProfile?>(null);
        public Task<GroundingProviderProfile> CreateCustomGroundingProviderProfileAsync(GroundingProviderProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<GroundingProviderProfile> UpdateCustomGroundingProviderProfileAsync(GroundingProviderProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomGroundingProviderProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<CrewSnapshot> ResolveSnapshotAsync(string? name, CrewSpec? spec, CancellationToken ct = default)
            => Task.FromResult(new CrewSnapshot(1, "klassik", SystemCrew.DefaultExecutorProfile, [], EvaluationStrategy.Parallel, null, []));
    }
}
