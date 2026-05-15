using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class GroundingProvidersIndexTests : TestContext
{
    private static GroundingProviderProfile MakeGrounding(
        string name, string providerType = "tavily", bool isSystem = false,
        Dictionary<string, string>? settings = null) =>
        new(Name: name, DisplayName: name + " Display", Description: "desc",
            ProviderType: providerType, ProviderSettings: settings ?? new(),
            MaxQueriesPerRun: 1, IsSystem: isSystem);

    [Fact]
    public void EmptyList_ShowsEmptyStateText()
    {
        Services.AddSingleton<ICrewService>(new StubCrewService([]));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProvidersIndex>();

        Assert.Contains("No grounding provider profiles yet.", cut.Markup);
    }

    [Fact]
    public void EmptyList_TableNotRendered()
    {
        Services.AddSingleton<ICrewService>(new StubCrewService([]));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProvidersIndex>();

        Assert.Throws<Bunit.ElementNotFoundException>(
            () => cut.Find("[data-testid='grounding-provider-list']"));
    }

    [Fact]
    public void WithProfiles_TableRendered()
    {
        var profiles = new[] { MakeGrounding("tavily-basic", isSystem: true) };
        Services.AddSingleton<ICrewService>(new StubCrewService(profiles));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProvidersIndex>();

        cut.Find("[data-testid='grounding-provider-list']");
    }

    [Fact]
    public void SystemProfile_RowHasViewLink()
    {
        var profiles = new[] { MakeGrounding("tavily-basic", isSystem: true) };
        Services.AddSingleton<ICrewService>(new StubCrewService(profiles));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProvidersIndex>();

        var row = cut.Find("[data-testid='grounding-provider-row-tavily-basic']");
        var viewLink = row.QuerySelector("a[href*='/view/tavily-basic']");
        Assert.NotNull(viewLink);
        Assert.Contains("/view/tavily-basic", viewLink.GetAttribute("href"));
    }

    [Fact]
    public void SystemProfile_DeleteButtonIsDisabled()
    {
        var profiles = new[] { MakeGrounding("tavily-basic", isSystem: true) };
        Services.AddSingleton<ICrewService>(new StubCrewService(profiles));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProvidersIndex>();

        var row = cut.Find("[data-testid='grounding-provider-row-tavily-basic']");
        var deleteBtn = row.QuerySelector("button[disabled]");
        Assert.NotNull(deleteBtn);
    }

    [Fact]
    public void SystemProfile_DuplicateButtonPresent()
    {
        var profiles = new[] { MakeGrounding("tavily-basic", isSystem: true) };
        Services.AddSingleton<ICrewService>(new StubCrewService(profiles));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProvidersIndex>();

        var row = cut.Find("[data-testid='grounding-provider-row-tavily-basic']");
        var buttons = row.QuerySelectorAll("button");
        Assert.Contains(buttons, b => b.TextContent.Contains("Duplicate"));
    }

    [Fact]
    public void CustomProfile_RowHasEditLink()
    {
        var profiles = new[] { MakeGrounding("custom-foo", isSystem: false) };
        Services.AddSingleton<ICrewService>(new StubCrewService(profiles));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProvidersIndex>();

        var row = cut.Find("[data-testid='grounding-provider-row-custom-foo']");
        var editLink = row.QuerySelector("a[href*='/edit/custom-foo']");
        Assert.NotNull(editLink);
        Assert.Contains("/edit/custom-foo", editLink.GetAttribute("href"));
    }

    [Fact]
    public void CustomProfile_RowHasDeleteButton()
    {
        var profiles = new[] { MakeGrounding("custom-foo", isSystem: false) };
        Services.AddSingleton<ICrewService>(new StubCrewService(profiles));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProvidersIndex>();

        var row = cut.Find("[data-testid='grounding-provider-row-custom-foo']");
        var deleteBtn = row.QuerySelector("button");
        Assert.NotNull(deleteBtn);
        Assert.Contains("Delete", deleteBtn.TextContent);
    }

    [Fact]
    public void SystemProfile_ShowsSystemBadge()
    {
        var profiles = new[] { MakeGrounding("tavily-basic", isSystem: true) };
        Services.AddSingleton<ICrewService>(new StubCrewService(profiles));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProvidersIndex>();

        var row = cut.Find("[data-testid='grounding-provider-row-tavily-basic']");
        Assert.Contains("System", row.TextContent);
    }

    [Fact]
    public void CustomProfile_NoSystemBadge()
    {
        var profiles = new[] { MakeGrounding("custom-foo", isSystem: false) };
        Services.AddSingleton<ICrewService>(new StubCrewService(profiles));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProvidersIndex>();

        var row = cut.Find("[data-testid='grounding-provider-row-custom-foo']");
        Assert.DoesNotContain("System", row.TextContent);
    }

    [Fact]
    public void DeleteButtonClick_ShowsDeleteConfirmModal()
    {
        var profiles = new[] { MakeGrounding("custom-foo", isSystem: false) };
        Services.AddSingleton<ICrewService>(new StubCrewService(profiles));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProvidersIndex>();
        var row = cut.Find("[data-testid='grounding-provider-row-custom-foo']");
        var deleteBtn = row.QuerySelector("button");
        Assert.NotNull(deleteBtn);

        deleteBtn.Click();

        // After clicking Delete, the modal should appear (Show=true => delete-confirm-input rendered)
        cut.Find("[data-testid='delete-confirm-input']");
    }

    private sealed class StubCrewService(IEnumerable<GroundingProviderProfile>? grounding = null) : ICrewService
    {
        private readonly List<GroundingProviderProfile> _groundingProfiles = grounding?.ToList() ?? [];
        public GroundingProviderProfile? LastCreated { get; private set; }

        public Task<IReadOnlyList<GroundingProviderProfile>> ListGroundingProviderProfilesAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GroundingProviderProfile>>(_groundingProfiles);
        public Task<GroundingProviderProfile?> GetGroundingProviderProfileAsync(string name, CancellationToken ct = default)
            => Task.FromResult(_groundingProfiles.FirstOrDefault(p => p.Name == name));
        public Task<GroundingProviderProfile> CreateCustomGroundingProviderProfileAsync(GroundingProviderProfile profile, CancellationToken ct = default)
        { LastCreated = profile; _groundingProfiles.Add(profile); return Task.FromResult(profile); }
        public Task<GroundingProviderProfile> UpdateCustomGroundingProviderProfileAsync(GroundingProviderProfile profile, CancellationToken ct = default)
            => Task.FromResult(profile);
        public Task DeleteCustomGroundingProviderProfileAsync(string name, CancellationToken ct = default)
        { _groundingProfiles.RemoveAll(p => p.Name == name); return Task.CompletedTask; }

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
        public Task<CrewSnapshot> ResolveSnapshotAsync(string? name, CrewSpec? spec, CancellationToken ct = default)
            => Task.FromResult(new CrewSnapshot(1, "klassik", SystemCrew.DefaultExecutorProfile, [], EvaluationStrategy.Parallel, null, []));
    }
}
