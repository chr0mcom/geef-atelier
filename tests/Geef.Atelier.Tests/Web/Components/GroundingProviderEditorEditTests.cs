using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Infrastructure.Grounding;
using Geef.Atelier.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class GroundingProviderEditorEditTests : TestContext
{
    private static GroundingProviderProfile MakeGrounding(
        string name, string providerType = "tavily", bool isSystem = false,
        Dictionary<string, string>? settings = null) =>
        new(Name: name, DisplayName: name + " Display", Description: "desc",
            ProviderType: providerType, ProviderSettings: settings ?? new(),
            MaxQueriesPerRun: 2, IsSystem: isSystem);

    [Fact]
    public void EditMode_NameInputIsEditable()
    {
        var profile = MakeGrounding("custom-tavily-adv", isSystem: false);
        Services.AddSingleton<ICrewService>(new StubCrewService(profile));
        Services.AddSingleton<IOptions<TavilyOptions>>(Options.Create(new TavilyOptions { ApiKey = "test-key" }));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProviderEditor>(p => p.Add(c => c.Name, "custom-tavily-adv"));

        var nameInput = cut.Find("[data-testid='input-name']");
        Assert.Null(nameInput.GetAttribute("disabled"));
    }

    [Fact]
    public void EditMode_ProviderTypeInputIsDisabled()
    {
        var profile = MakeGrounding("custom-tavily-adv", isSystem: false);
        Services.AddSingleton<ICrewService>(new StubCrewService(profile));
        Services.AddSingleton<IOptions<TavilyOptions>>(Options.Create(new TavilyOptions { ApiKey = "test-key" }));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProviderEditor>(p => p.Add(c => c.Name, "custom-tavily-adv"));

        var providerTypeInput = cut.Find("[data-testid='input-provider-type']");
        Assert.NotNull(providerTypeInput.GetAttribute("disabled"));
    }

    [Fact]
    public void EditMode_LoadsDisplayNameIntoInput()
    {
        var profile = MakeGrounding("custom-tavily-adv", isSystem: false);
        Services.AddSingleton<ICrewService>(new StubCrewService(profile));
        Services.AddSingleton<IOptions<TavilyOptions>>(Options.Create(new TavilyOptions { ApiKey = "test-key" }));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProviderEditor>(p => p.Add(c => c.Name, "custom-tavily-adv"));

        var displayNameInput = cut.Find("[data-testid='input-display-name']");
        Assert.Equal("custom-tavily-adv Display", displayNameInput.GetAttribute("value"));
    }

    [Fact]
    public void EditMode_SystemProfile_RedirectsToViewPage()
    {
        var profile = MakeGrounding("tavily-basic", isSystem: true);
        Services.AddSingleton<ICrewService>(new StubCrewService(profile));
        Services.AddSingleton<IOptions<TavilyOptions>>(Options.Create(new TavilyOptions { ApiKey = "test-key" }));
        this.AddTestAuthorization().SetAuthorized("test-user");

        RenderComponent<GroundingProviderEditor>(p => p.Add(c => c.Name, "tavily-basic"));

        var nav = Services.GetRequiredService<NavigationManager>();
        Assert.Contains("/view/", nav.Uri);
    }

    [Fact]
    public void EditMode_ShowsDeleteButton()
    {
        var profile = MakeGrounding("custom-tavily-adv", isSystem: false);
        Services.AddSingleton<ICrewService>(new StubCrewService(profile));
        Services.AddSingleton<IOptions<TavilyOptions>>(Options.Create(new TavilyOptions { ApiKey = "test-key" }));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProviderEditor>(p => p.Add(c => c.Name, "custom-tavily-adv"));

        cut.Find("[data-testid='btn-delete']");
    }

    [Fact]
    public void CreateMode_NoDeleteButton()
    {
        Services.AddSingleton<ICrewService>(new StubCrewService(null));
        Services.AddSingleton<IOptions<TavilyOptions>>(Options.Create(new TavilyOptions { ApiKey = "test-key" }));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProviderEditor>();

        Assert.Empty(cut.FindAll("[data-testid='btn-delete']"));
    }

    private sealed class StubCrewService(GroundingProviderProfile? profile) : ICrewService
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
