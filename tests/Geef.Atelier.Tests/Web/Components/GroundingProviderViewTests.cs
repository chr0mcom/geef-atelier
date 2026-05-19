using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class GroundingProviderViewTests : TestContext
{
    private static GroundingProviderProfile MakeGrounding(
        string name, string providerType = "tavily", bool isSystem = true,
        Dictionary<string, string>? settings = null) =>
        new(Name: name, DisplayName: name + " Display Name", Description: "A test description.",
            ProviderType: providerType, ProviderSettings: settings ?? new(),
            MaxQueriesPerRun: 1, IsSystem: isSystem);

    [Fact]
    public void RendersDataTestIdGroundingProviderView()
    {
        var profile = MakeGrounding("tavily-basic", isSystem: true);
        Services.AddSingleton<ICrewService>(new StubCrewService(profile));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProviderView>(p => p.Add(c => c.Name, "tavily-basic"));

        cut.Find("[data-testid='grounding-provider-view']");
    }

    [Fact]
    public void ShowsProfileDisplayNameInHeading()
    {
        var profile = MakeGrounding("tavily-basic", isSystem: true);
        Services.AddSingleton<ICrewService>(new StubCrewService(profile));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProviderView>(p => p.Add(c => c.Name, "tavily-basic"));

        var heading = cut.Find("h1");
        Assert.Contains("tavily-basic Display Name", heading.TextContent);
    }

    [Fact]
    public void ShowsSystemProfileInfoBanner()
    {
        var profile = MakeGrounding("tavily-basic", isSystem: true);
        Services.AddSingleton<ICrewService>(new StubCrewService(profile));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProviderView>(p => p.Add(c => c.Name, "tavily-basic"));

        var markup = cut.Markup;
        Assert.Contains("system profile", markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShowsDetailName()
    {
        var profile = MakeGrounding("tavily-basic", isSystem: true);
        Services.AddSingleton<ICrewService>(new StubCrewService(profile));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProviderView>(p => p.Add(c => c.Name, "tavily-basic"));

        var nameEl = cut.Find("[data-testid='detail-name']");
        Assert.Contains("tavily-basic", nameEl.TextContent);
    }

    [Fact]
    public void ShowsDetailDisplayName()
    {
        var profile = MakeGrounding("tavily-basic", isSystem: true);
        Services.AddSingleton<ICrewService>(new StubCrewService(profile));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProviderView>(p => p.Add(c => c.Name, "tavily-basic"));

        var el = cut.Find("[data-testid='detail-display-name']");
        Assert.Contains("tavily-basic Display Name", el.TextContent);
    }

    [Fact]
    public void ShowsDetailDescription()
    {
        var profile = MakeGrounding("tavily-basic", isSystem: true);
        Services.AddSingleton<ICrewService>(new StubCrewService(profile));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProviderView>(p => p.Add(c => c.Name, "tavily-basic"));

        var el = cut.Find("[data-testid='detail-description']");
        Assert.Contains("A test description.", el.TextContent);
    }

    [Fact]
    public void ShowsDetailProviderType()
    {
        var profile = MakeGrounding("tavily-basic", isSystem: true);
        Services.AddSingleton<ICrewService>(new StubCrewService(profile));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProviderView>(p => p.Add(c => c.Name, "tavily-basic"));

        cut.Find("[data-testid='detail-provider-type']");
    }

    [Fact]
    public void ViewPage_HasNoEditableInputs()
    {
        var profile = MakeGrounding("tavily-basic", isSystem: true);
        Services.AddSingleton<ICrewService>(new StubCrewService(profile));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProviderView>(p => p.Add(c => c.Name, "tavily-basic"));

        Assert.Empty(cut.FindAll("input"));
        Assert.Empty(cut.FindAll("select"));
        Assert.Empty(cut.FindAll("textarea"));
    }

    [Fact]
    public void NullProfile_NavigatesToIndex()
    {
        // Stub returns null for any name
        Services.AddSingleton<ICrewService>(new StubCrewService(null));
        this.AddTestAuthorization().SetAuthorized("test-user");

        RenderComponent<GroundingProviderView>(p => p.Add(c => c.Name, "does-not-exist"));

        var nav = Services.GetRequiredService<NavigationManager>();
        Assert.Contains("/crew/profiles/grounding-providers", nav.Uri);
    }

    private sealed class StubCrewService(GroundingProviderProfile? profile) : ICrewService
    {
        public Task<IReadOnlyList<GroundingProviderProfile>> ListGroundingProviderProfilesAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GroundingProviderProfile>>(profile != null ? [profile] : []);
        public Task<GroundingProviderProfile?> GetGroundingProviderProfileAsync(string name, CancellationToken ct = default)
            => Task.FromResult(profile?.Name == name ? profile : null);
        public Task<GroundingProviderProfile> CreateCustomGroundingProviderProfileAsync(GroundingProviderProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<GroundingProviderProfile> UpdateCustomGroundingProviderProfileAsync(GroundingProviderProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomGroundingProviderProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;

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
        public Task<string> RenameCustomReviewerProfileAsync(string oldName, string newName, CancellationToken ct = default) => Task.FromResult(newName);
        public Task<string> RenameCustomExecutorProfileAsync(string oldName, string newName, CancellationToken ct = default) => Task.FromResult(newName);
        public Task<string> RenameCustomAdvisorProfileAsync(string oldName, string newName, CancellationToken ct = default) => Task.FromResult(newName);
        public Task<string> RenameCustomGroundingProviderProfileAsync(string oldName, string newName, CancellationToken ct = default) => Task.FromResult(newName);
        public Task<IReadOnlyList<FinalizerProfile>> ListFinalizerProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<FinalizerProfile>>([]);
        public Task<FinalizerProfile?> GetFinalizerProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<FinalizerProfile?>(null);
        public Task<FinalizerProfile> CreateCustomFinalizerProfileAsync(FinalizerProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task<FinalizerProfile> UpdateCustomFinalizerProfileAsync(FinalizerProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task DeleteCustomFinalizerProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomFinalizerProfileAsync(string oldName, string newName, CancellationToken ct = default) => Task.FromResult(newName);
        public Task<string> RenameCustomCrewTemplateAsync(string oldName, string newName, CancellationToken ct = default) => Task.FromResult(newName);
        public Task<CrewSnapshot> ResolveSnapshotAsync(string? name, CrewSpec? spec, CancellationToken ct = default)
            => Task.FromResult(new CrewSnapshot(1, "klassik", SystemCrew.DefaultExecutorProfile, [], EvaluationStrategy.Parallel, null, []));
    }
}
