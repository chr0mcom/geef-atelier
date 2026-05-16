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

public sealed class GroundingProviderScopeTests : TestContext
{
    private static GroundingProviderProfile MakeVectorStoreProfile(
        string name, string? scope, bool isSystem = false)
    {
        var settings = new Dictionary<string, string> { ["TopK"] = "5" };
        if (scope != null) settings["Scope"] = scope;
        return new(Name: name, DisplayName: name + " Display", Description: "desc",
            ProviderType: "vector-store", ProviderSettings: settings,
            MaxQueriesPerRun: 1, IsSystem: isSystem);
    }

    [Fact]
    public void CreateForm_WhenSwitchedToVectorStore_ScopeDefaultsToGlobal()
    {
        Services.AddSingleton<ICrewService>(new StubCrewService(null));
        Services.AddSingleton<IOptions<TavilyOptions>>(Options.Create(new TavilyOptions { ApiKey = "test-key" }));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProviderEditor>();
        cut.Find("[data-testid='input-provider-type']").Change("vector-store");

        var scopeSelect = cut.Find("[data-testid='input-scope']");
        Assert.NotNull(scopeSelect);
        // The GroundingFormModel.Scope defaults to "global"
        Assert.Equal("global", scopeSelect.GetAttribute("value"));
    }

    [Fact]
    public void EditForm_ProfileWithScopeRunLocal_ShowsRunLocal()
    {
        var profile = MakeVectorStoreProfile("custom-vs", "run-local");
        Services.AddSingleton<ICrewService>(new StubCrewService(profile));
        Services.AddSingleton<IOptions<TavilyOptions>>(Options.Create(new TavilyOptions { ApiKey = "test-key" }));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProviderEditor>(p => p.Add(c => c.Name, "custom-vs"));

        var scopeSelect = cut.Find("[data-testid='input-scope']");
        Assert.Equal("run-local", scopeSelect.GetAttribute("value"));
    }

    [Fact]
    public void EditForm_ProfileWithoutScopeKey_FallsBackToBoth()
    {
        // Profile has no "Scope" key in ProviderSettings (legacy profile)
        var profile = MakeVectorStoreProfile("custom-vs-legacy", scope: null);
        Services.AddSingleton<ICrewService>(new StubCrewService(profile));
        Services.AddSingleton<IOptions<TavilyOptions>>(Options.Create(new TavilyOptions { ApiKey = "test-key" }));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProviderEditor>(p => p.Add(c => c.Name, "custom-vs-legacy"));

        var scopeSelect = cut.Find("[data-testid='input-scope']");
        // GroundingFormModel.From() uses `scope ?? "both"` when key is absent
        Assert.Equal("both", scopeSelect.GetAttribute("value"));
    }

    [Fact]
    public void ScopeSelect_HasThreeOptions_GlobalRunLocalBoth()
    {
        Services.AddSingleton<ICrewService>(new StubCrewService(null));
        Services.AddSingleton<IOptions<TavilyOptions>>(Options.Create(new TavilyOptions { ApiKey = "test-key" }));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProviderEditor>();
        cut.Find("[data-testid='input-provider-type']").Change("vector-store");

        var options = cut.FindAll("[data-testid='input-scope'] option");
        Assert.Equal(3, options.Count);
        Assert.Contains(options, o => o.GetAttribute("value") == "global");
        Assert.Contains(options, o => o.GetAttribute("value") == "run-local");
        Assert.Contains(options, o => o.GetAttribute("value") == "both");
    }

    [Fact]
    public void EditForm_ProfileWithScopeGlobal_ShowsGlobal()
    {
        var profile = MakeVectorStoreProfile("custom-vs-global", "global");
        Services.AddSingleton<ICrewService>(new StubCrewService(profile));
        Services.AddSingleton<IOptions<TavilyOptions>>(Options.Create(new TavilyOptions { ApiKey = "test-key" }));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProviderEditor>(p => p.Add(c => c.Name, "custom-vs-global"));

        var scopeSelect = cut.Find("[data-testid='input-scope']");
        Assert.Equal("global", scopeSelect.GetAttribute("value"));
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
        public Task<string> RenameCustomCrewTemplateAsync(string oldName, string newName, CancellationToken ct = default) => Task.FromResult(newName);
        public Task<CrewSnapshot> ResolveSnapshotAsync(string? name, CrewSpec? spec, CancellationToken ct = default)
            => Task.FromResult(new CrewSnapshot(1, "klassik", SystemCrew.DefaultExecutorProfile, [], EvaluationStrategy.Parallel, null, []));
    }
}
