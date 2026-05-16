using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Infrastructure.Grounding;
using Geef.Atelier.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class GroundingProviderEditorCreateTests : TestContext
{
    private static GroundingProviderProfile MakeGrounding(
        string name, string providerType = "tavily", bool isSystem = false,
        Dictionary<string, string>? settings = null) =>
        new(Name: name, DisplayName: name + " Display", Description: "desc",
            ProviderType: providerType, ProviderSettings: settings ?? new(),
            MaxQueriesPerRun: 1, IsSystem: isSystem);

    [Fact]
    public void RendersDataTestIdGroundingProviderEditor()
    {
        Services.AddSingleton<ICrewService>(new StubCrewService());
        Services.AddSingleton<IOptions<TavilyOptions>>(Options.Create(new TavilyOptions { ApiKey = "test-key" }));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProviderEditor>();

        cut.Find("[data-testid='grounding-provider-editor']");
    }

    [Fact]
    public void CreateForm_DefaultProviderType_IsTavily_ShowsTavilyFields()
    {
        Services.AddSingleton<ICrewService>(new StubCrewService());
        Services.AddSingleton<IOptions<TavilyOptions>>(Options.Create(new TavilyOptions { ApiKey = "test-key" }));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProviderEditor>();

        // Tavily-specific fields should be visible by default
        cut.Find("[data-testid='input-tier']");
        cut.Find("[data-testid='input-max-results']");
        cut.Find("[data-testid='input-include-answer']");
    }

    [Fact]
    public void CreateForm_WhenProviderTypeSwitchedToVectorStore_ShowsVectorStoreFields()
    {
        Services.AddSingleton<ICrewService>(new StubCrewService());
        Services.AddSingleton<IOptions<TavilyOptions>>(Options.Create(new TavilyOptions { ApiKey = "test-key" }));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProviderEditor>();
        cut.Find("[data-testid='input-provider-type']").Change("vector-store");

        cut.Find("[data-testid='input-top-k']");
        cut.Find("[data-testid='input-tag-filter']");
        cut.Find("[data-testid='input-scope']");
    }

    [Fact]
    public void CreateForm_WhenProviderTypeSwitchedToVectorStore_TavilyFieldsHidden()
    {
        Services.AddSingleton<ICrewService>(new StubCrewService());
        Services.AddSingleton<IOptions<TavilyOptions>>(Options.Create(new TavilyOptions { ApiKey = "test-key" }));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProviderEditor>();
        cut.Find("[data-testid='input-provider-type']").Change("vector-store");

        Assert.Empty(cut.FindAll("[data-testid='input-tier']"));
        Assert.Empty(cut.FindAll("[data-testid='input-max-results']"));
    }

    [Fact]
    public void TavilyKeyWarning_ShownWhenApiKeyIsEmpty()
    {
        Services.AddSingleton<ICrewService>(new StubCrewService());
        Services.AddSingleton<IOptions<TavilyOptions>>(Options.Create(new TavilyOptions()));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProviderEditor>();

        cut.Find("[data-testid='tavily-key-warning']");
    }

    [Fact]
    public void TavilyKeyWarning_NotShownWhenApiKeyIsSet()
    {
        Services.AddSingleton<ICrewService>(new StubCrewService());
        Services.AddSingleton<IOptions<TavilyOptions>>(Options.Create(new TavilyOptions { ApiKey = "sk-test" }));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProviderEditor>();

        Assert.Empty(cut.FindAll("[data-testid='tavily-key-warning']"));
    }

    [Fact]
    public void MaxQueriesPerRun_DefaultsToOne()
    {
        Services.AddSingleton<ICrewService>(new StubCrewService());
        Services.AddSingleton<IOptions<TavilyOptions>>(Options.Create(new TavilyOptions { ApiKey = "test-key" }));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProviderEditor>();

        var maxInput = cut.Find("[data-testid='input-max-queries']");
        Assert.Equal("1", maxInput.GetAttribute("value"));
    }

    [Fact]
    public void NameInput_HasPlaceholder_LegalDocsSearch()
    {
        Services.AddSingleton<ICrewService>(new StubCrewService());
        Services.AddSingleton<IOptions<TavilyOptions>>(Options.Create(new TavilyOptions { ApiKey = "test-key" }));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProviderEditor>();

        var nameInput = cut.Find("[data-testid='input-name']");
        Assert.Equal("legal-docs-search", nameInput.GetAttribute("placeholder"));
    }

    private sealed class StubCrewService(IEnumerable<GroundingProviderProfile>? grounding = null) : ICrewService
    {
        private readonly List<GroundingProviderProfile> _groundingProfiles = grounding?.ToList() ?? [];

        public Task<IReadOnlyList<GroundingProviderProfile>> ListGroundingProviderProfilesAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GroundingProviderProfile>>(_groundingProfiles);
        public Task<GroundingProviderProfile?> GetGroundingProviderProfileAsync(string name, CancellationToken ct = default)
            => Task.FromResult(_groundingProfiles.FirstOrDefault(p => p.Name == name));
        public Task<GroundingProviderProfile> CreateCustomGroundingProviderProfileAsync(GroundingProviderProfile profile, CancellationToken ct = default)
        { _groundingProfiles.Add(profile); return Task.FromResult(profile); }
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
        public Task<string> RenameCustomReviewerProfileAsync(string oldName, string newName, CancellationToken ct = default) => Task.FromResult(newName);
        public Task<string> RenameCustomExecutorProfileAsync(string oldName, string newName, CancellationToken ct = default) => Task.FromResult(newName);
        public Task<string> RenameCustomAdvisorProfileAsync(string oldName, string newName, CancellationToken ct = default) => Task.FromResult(newName);
        public Task<string> RenameCustomGroundingProviderProfileAsync(string oldName, string newName, CancellationToken ct = default) => Task.FromResult(newName);
        public Task<string> RenameCustomCrewTemplateAsync(string oldName, string newName, CancellationToken ct = default) => Task.FromResult(newName);
        public Task<CrewSnapshot> ResolveSnapshotAsync(string? name, CrewSpec? spec, CancellationToken ct = default)
            => Task.FromResult(new CrewSnapshot(1, "klassik", SystemCrew.DefaultExecutorProfile, [], EvaluationStrategy.Parallel, null, []));
    }
}
