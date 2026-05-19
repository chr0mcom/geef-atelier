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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class MaxQueriesPerRunValidationTests : TestContext
{
    [Fact]
    public void CreateForm_MaxQueriesPerRunInput_DefaultsToOne()
    {
        Services.AddSingleton<ICrewService>(new StubCrewService());
        Services.AddSingleton<IOptions<TavilyOptions>>(Options.Create(new TavilyOptions { ApiKey = "test-key" }));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProviderEditor>();

        var maxInput = cut.Find("[data-testid='input-max-queries']");
        Assert.Equal("1", maxInput.GetAttribute("value"));
    }

    [Fact]
    public void CreateForm_MaxQueriesPerRunHint_ContainsExpectedText()
    {
        Services.AddSingleton<ICrewService>(new StubCrewService());
        Services.AddSingleton<IOptions<TavilyOptions>>(Options.Create(new TavilyOptions { ApiKey = "test-key" }));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProviderEditor>();

        // Hint text says "1–5, default 1" (from the razor: <small class="form-hint">1–5, default 1. Leave empty for no limit.</small>)
        Assert.Contains("1–5", cut.Markup);
        Assert.Contains("default 1", cut.Markup);
    }

    [Fact]
    public void CreateForm_MaxQueriesInput_IsNumberType()
    {
        Services.AddSingleton<ICrewService>(new StubCrewService());
        Services.AddSingleton<IOptions<TavilyOptions>>(Options.Create(new TavilyOptions { ApiKey = "test-key" }));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProviderEditor>();

        var maxInput = cut.Find("[data-testid='input-max-queries']");
        Assert.Equal("number", maxInput.GetAttribute("type"));
    }

    [Fact]
    public void CreateForm_MaxQueriesInput_Exists()
    {
        Services.AddSingleton<ICrewService>(new StubCrewService());
        Services.AddSingleton<IOptions<TavilyOptions>>(Options.Create(new TavilyOptions { ApiKey = "test-key" }));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<GroundingProviderEditor>();

        // Verify the element is present and accessible
        var maxInput = cut.Find("[data-testid='input-max-queries']");
        Assert.NotNull(maxInput);
    }

    private sealed class StubCrewService(IEnumerable<GroundingProviderProfile>? grounding = null) : ICrewService
    {
        private readonly List<GroundingProviderProfile> _profiles = grounding?.ToList() ?? [];

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
