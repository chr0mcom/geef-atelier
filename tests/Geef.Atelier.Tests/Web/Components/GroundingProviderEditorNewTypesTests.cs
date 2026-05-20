using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Providers;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Infrastructure.Grounding;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class GroundingProviderEditorNewTypesTests : TestContext
{
    private void SetupServices(string apiKey = "test-key")
    {
        Services.AddSingleton<ICrewService>(new StubCrewService());
        Services.AddSingleton<IProviderService>(new FakeProviderService());
        Services.AddSingleton<IOptions<TavilyOptions>>(Options.Create(new TavilyOptions { ApiKey = apiKey }));
        this.AddTestAuthorization().SetAuthorized("test-user");
    }

    // ── Provider type dropdown ────────────────────────────────────────────────

    [Fact]
    public void TypeDropdown_ContainsTavilyOption()
    {
        SetupServices();
        var cut = RenderComponent<GroundingProviderEditor>();
        var select = cut.Find("[data-testid='input-provider-type']");
        Assert.Contains("tavily", select.InnerHtml);
    }

    [Fact]
    public void TypeDropdown_ContainsVectorStoreOption()
    {
        SetupServices();
        var cut = RenderComponent<GroundingProviderEditor>();
        var select = cut.Find("[data-testid='input-provider-type']");
        Assert.Contains("vector-store", select.InnerHtml);
    }

    [Fact]
    public void TypeDropdown_ContainsStaticContextOption()
    {
        SetupServices();
        var cut = RenderComponent<GroundingProviderEditor>();
        var select = cut.Find("[data-testid='input-provider-type']");
        Assert.Contains("static-context", select.InnerHtml);
    }

    [Fact]
    public void TypeDropdown_ContainsUrlFetchOption()
    {
        SetupServices();
        var cut = RenderComponent<GroundingProviderEditor>();
        var select = cut.Find("[data-testid='input-provider-type']");
        Assert.Contains("url-fetch", select.InnerHtml);
    }

    [Fact]
    public void TypeDropdown_ContainsNewsSearchOption()
    {
        SetupServices();
        var cut = RenderComponent<GroundingProviderEditor>();
        var select = cut.Find("[data-testid='input-provider-type']");
        Assert.Contains("news-search", select.InnerHtml);
    }

    // ── static-context ────────────────────────────────────────────────────────

    [Fact]
    public void StaticContext_ShowsStaticLabelField()
    {
        SetupServices();
        var cut = RenderComponent<GroundingProviderEditor>();
        cut.Find("[data-testid='input-provider-type']").Change("static-context");
        cut.Find("[data-testid='input-static-label']");
    }

    [Fact]
    public void StaticContext_ShowsStaticContentField()
    {
        SetupServices();
        var cut = RenderComponent<GroundingProviderEditor>();
        cut.Find("[data-testid='input-provider-type']").Change("static-context");
        cut.Find("[data-testid='input-static-content']");
    }

    [Fact]
    public void StaticContext_HidesMaxQueriesPerRunField()
    {
        SetupServices();
        var cut = RenderComponent<GroundingProviderEditor>();
        cut.Find("[data-testid='input-provider-type']").Change("static-context");
        // input-max-queries is hidden for static-context (always 1, no user input)
        Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find("[data-testid='input-max-queries']"));
    }

    // ── url-fetch ─────────────────────────────────────────────────────────────

    [Fact]
    public void UrlFetch_ShowsUrlsField()
    {
        SetupServices();
        var cut = RenderComponent<GroundingProviderEditor>();
        cut.Find("[data-testid='input-provider-type']").Change("url-fetch");
        cut.Find("[data-testid='input-urls']");
    }

    [Fact]
    public void UrlFetch_ShowsMaxContentPerUrlField()
    {
        SetupServices();
        var cut = RenderComponent<GroundingProviderEditor>();
        cut.Find("[data-testid='input-provider-type']").Change("url-fetch");
        cut.Find("[data-testid='input-max-content-per-url']");
    }

    [Fact]
    public void UrlFetch_ShowsMaxQueriesPerRunField()
    {
        SetupServices();
        var cut = RenderComponent<GroundingProviderEditor>();
        cut.Find("[data-testid='input-provider-type']").Change("url-fetch");
        cut.Find("[data-testid='input-max-queries']");
    }

    // ── news-search ───────────────────────────────────────────────────────────

    [Fact]
    public void NewsSearch_ShowsRecencyDaysField()
    {
        SetupServices();
        var cut = RenderComponent<GroundingProviderEditor>();
        cut.Find("[data-testid='input-provider-type']").Change("news-search");
        cut.Find("[data-testid='input-recency-days']");
    }

    [Fact]
    public void NewsSearch_ShowsNewsMaxResultsField()
    {
        SetupServices();
        var cut = RenderComponent<GroundingProviderEditor>();
        cut.Find("[data-testid='input-provider-type']").Change("news-search");
        cut.Find("[data-testid='input-news-max-results']");
    }

    [Fact]
    public void NewsSearch_ShowsMaxQueriesPerRunField()
    {
        SetupServices();
        var cut = RenderComponent<GroundingProviderEditor>();
        cut.Find("[data-testid='input-provider-type']").Change("news-search");
        cut.Find("[data-testid='input-max-queries']");
    }

    // ── Stub ──────────────────────────────────────────────────────────────────

    private sealed class StubCrewService : ICrewService
    {
        public Task<IReadOnlyList<GroundingProviderProfile>> ListGroundingProviderProfilesAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GroundingProviderProfile>>([]);
        public Task<GroundingProviderProfile?> GetGroundingProviderProfileAsync(string name, CancellationToken ct = default)
            => Task.FromResult<GroundingProviderProfile?>(null);
        public Task<GroundingProviderProfile> CreateCustomGroundingProviderProfileAsync(GroundingProviderProfile profile, CancellationToken ct = default)
            => Task.FromResult(profile);
        public Task<GroundingProviderProfile> UpdateCustomGroundingProviderProfileAsync(GroundingProviderProfile profile, CancellationToken ct = default)
            => Task.FromResult(profile);
        public Task DeleteCustomGroundingProviderProfileAsync(string name, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<IReadOnlyList<ReviewerProfile>> ListReviewerProfilesAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ReviewerProfile>>([]);
        public Task<ReviewerProfile?> GetReviewerProfileAsync(string name, CancellationToken ct = default)
            => Task.FromResult<ReviewerProfile?>(null);
        public Task<ReviewerProfile> CreateCustomReviewerProfileAsync(ReviewerProfile p, CancellationToken ct = default)
            => Task.FromResult(p);
        public Task<ReviewerProfile> UpdateCustomReviewerProfileAsync(ReviewerProfile p, CancellationToken ct = default)
            => Task.FromResult(p);
        public Task DeleteCustomReviewerProfileAsync(string name, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<IReadOnlyList<ExecutorProfile>> ListExecutorProfilesAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ExecutorProfile>>([]);
        public Task<ExecutorProfile?> GetExecutorProfileAsync(string name, CancellationToken ct = default)
            => Task.FromResult<ExecutorProfile?>(null);
        public Task<ExecutorProfile> CreateCustomExecutorProfileAsync(ExecutorProfile p, CancellationToken ct = default)
            => Task.FromResult(p);
        public Task<ExecutorProfile> UpdateCustomExecutorProfileAsync(ExecutorProfile p, CancellationToken ct = default)
            => Task.FromResult(p);
        public Task DeleteCustomExecutorProfileAsync(string name, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<IReadOnlyList<AdvisorProfile>> ListAdvisorProfilesAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AdvisorProfile>>([]);
        public Task<AdvisorProfile?> GetAdvisorProfileAsync(string name, CancellationToken ct = default)
            => Task.FromResult<AdvisorProfile?>(null);
        public Task<AdvisorProfile> CreateCustomAdvisorProfileAsync(AdvisorProfile p, CancellationToken ct = default)
            => Task.FromResult(p);
        public Task<AdvisorProfile> UpdateCustomAdvisorProfileAsync(AdvisorProfile p, CancellationToken ct = default)
            => Task.FromResult(p);
        public Task DeleteCustomAdvisorProfileAsync(string name, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<IReadOnlyList<CrewTemplate>> ListCrewTemplatesAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CrewTemplate>>([]);
        public Task<CrewTemplate?> GetCrewTemplateAsync(string name, CancellationToken ct = default)
            => Task.FromResult<CrewTemplate?>(null);
        public Task<CrewTemplate> CreateCustomCrewTemplateAsync(CrewTemplate t, CancellationToken ct = default)
            => Task.FromResult(t);
        public Task<CrewTemplate> UpdateCustomCrewTemplateAsync(CrewTemplate t, CancellationToken ct = default)
            => Task.FromResult(t);
        public Task DeleteCustomCrewTemplateAsync(string name, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<string> RenameCustomReviewerProfileAsync(string oldName, string newName, CancellationToken ct = default)
            => Task.FromResult(newName);
        public Task<string> RenameCustomExecutorProfileAsync(string oldName, string newName, CancellationToken ct = default)
            => Task.FromResult(newName);
        public Task<string> RenameCustomAdvisorProfileAsync(string oldName, string newName, CancellationToken ct = default)
            => Task.FromResult(newName);
        public Task<string> RenameCustomGroundingProviderProfileAsync(string oldName, string newName, CancellationToken ct = default)
            => Task.FromResult(newName);
        public Task<IReadOnlyList<FinalizerProfile>> ListFinalizerProfilesAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FinalizerProfile>>([]);
        public Task<FinalizerProfile?> GetFinalizerProfileAsync(string name, CancellationToken ct = default)
            => Task.FromResult<FinalizerProfile?>(null);
        public Task<FinalizerProfile> CreateCustomFinalizerProfileAsync(FinalizerProfile profile, CancellationToken ct = default)
            => Task.FromResult(profile);
        public Task<FinalizerProfile> UpdateCustomFinalizerProfileAsync(FinalizerProfile profile, CancellationToken ct = default)
            => Task.FromResult(profile);
        public Task DeleteCustomFinalizerProfileAsync(string name, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<string> RenameCustomFinalizerProfileAsync(string oldName, string newName, CancellationToken ct = default)
            => Task.FromResult(newName);
        public Task<string> RenameCustomCrewTemplateAsync(string oldName, string newName, CancellationToken ct = default)
            => Task.FromResult(newName);
        public Task<CrewSnapshot> ResolveSnapshotAsync(string? name, CrewSpec? spec, CancellationToken ct = default)
            => Task.FromResult(new CrewSnapshot(1, "klassik", SystemCrew.DefaultExecutorProfile, [], EvaluationStrategy.Parallel, null, []));
    }
}
