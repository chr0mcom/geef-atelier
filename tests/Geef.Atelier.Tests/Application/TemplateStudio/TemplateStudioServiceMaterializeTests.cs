using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Application.Crew.TemplateStudio;
using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.Crew.TemplateStudio;
using Geef.Atelier.Core.Persistence.TemplateStudio;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.TemplateStudio;
using Geef.Atelier.Tests.Llm;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Application.TemplateStudio;

public sealed class TemplateStudioServiceMaterializeTests
{
    // -------------------------------------------------------------------------
    // Test doubles (copied pattern from Analyze tests, trimmed for brevity)
    // -------------------------------------------------------------------------

    private sealed class CapturingCrewService : ICrewService
    {
        public readonly List<ReviewerProfile> CreatedReviewers = [];
        public readonly List<CrewTemplate> CreatedTemplates = [];

        public Task<IReadOnlyList<ReviewerProfile>> ListReviewerProfilesAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<ReviewerProfile>)SystemCrew.ReviewerProfiles.Values.ToList());
        public Task<IReadOnlyList<AdvisorProfile>> ListAdvisorProfilesAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<AdvisorProfile>)[]);
        public Task<IReadOnlyList<GroundingProviderProfile>> ListGroundingProviderProfilesAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<GroundingProviderProfile>)[]);
        public Task<IReadOnlyList<ExecutorProfile>> ListExecutorProfilesAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<ExecutorProfile>)[SystemCrew.DefaultExecutorProfile]);
        public Task<IReadOnlyList<CrewTemplate>> ListCrewTemplatesAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<CrewTemplate>)[SystemCrew.KlassikTemplate]);

        public Task<ReviewerProfile?> GetReviewerProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<ReviewerProfile?>(null);
        public Task<ExecutorProfile?> GetExecutorProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<ExecutorProfile?>(null);
        public Task<AdvisorProfile?> GetAdvisorProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<AdvisorProfile?>(null);
        public Task<GroundingProviderProfile?> GetGroundingProviderProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<GroundingProviderProfile?>(null);
        public Task<CrewTemplate?> GetCrewTemplateAsync(string name, CancellationToken ct = default) => Task.FromResult<CrewTemplate?>(null);

        public Task<ReviewerProfile> CreateCustomReviewerProfileAsync(ReviewerProfile profile, CancellationToken ct = default)
        {
            var withPrefix = profile with { Name = SystemCrew.EnsureCustomPrefix(profile.Name) };
            CreatedReviewers.Add(withPrefix);
            return Task.FromResult(withPrefix);
        }

        public Task<ReviewerProfile> UpdateCustomReviewerProfileAsync(ReviewerProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task DeleteCustomReviewerProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;

        public Task<ExecutorProfile> CreateCustomExecutorProfileAsync(ExecutorProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task<ExecutorProfile> UpdateCustomExecutorProfileAsync(ExecutorProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task DeleteCustomExecutorProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;

        public Task<AdvisorProfile> CreateCustomAdvisorProfileAsync(AdvisorProfile profile, CancellationToken ct = default)
        {
            var withPrefix = profile with { Name = SystemCrew.EnsureCustomPrefix(profile.Name) };
            return Task.FromResult(withPrefix);
        }
        public Task<AdvisorProfile> UpdateCustomAdvisorProfileAsync(AdvisorProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task DeleteCustomAdvisorProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;

        public Task<GroundingProviderProfile> CreateCustomGroundingProviderProfileAsync(GroundingProviderProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task<GroundingProviderProfile> UpdateCustomGroundingProviderProfileAsync(GroundingProviderProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task DeleteCustomGroundingProviderProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;

        public Task<CrewTemplate> CreateCustomCrewTemplateAsync(CrewTemplate template, CancellationToken ct = default)
        {
            var withPrefix = template with { Name = SystemCrew.EnsureCustomPrefix(template.Name) };
            CreatedTemplates.Add(withPrefix);
            return Task.FromResult(withPrefix);
        }

        public Task<CrewTemplate> UpdateCustomCrewTemplateAsync(CrewTemplate template, CancellationToken ct = default) => Task.FromResult(template);
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

        public Task<CrewSnapshot> ResolveSnapshotAsync(string? crewTemplateName, CrewSpec? customCrew, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class InMemoryAnalysisRepository : ITemplateStudioAnalysisRepository
    {
        private readonly List<TemplateStudioAnalysis> _store = [];
        public readonly Dictionary<Guid, string> Materialized = [];

        public Task CreateAsync(TemplateStudioAnalysis analysis, CancellationToken ct = default)
        {
            _store.Add(analysis);
            return Task.CompletedTask;
        }

        public Task<TemplateStudioAnalysis?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_store.FirstOrDefault(a => a.Id == id));

        public Task MarkMaterializedAsync(Guid analysisId, string templateName, CancellationToken ct = default)
        {
            Materialized[analysisId] = templateName;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TemplateStudioAnalysis>> ListRecentAsync(int limit = 10, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<TemplateStudioAnalysis>)_store.Take(limit).ToList());

        public Task<(IReadOnlyList<TemplateStudioHistoryItem> Items, bool HasMore)> ListHistoryAsync(int page, int pageSize, CancellationToken ct = default)
        {
            var skip = page * pageSize;
            var take = pageSize + 1;
            var slice = _store.Skip(skip).Take(take).ToList();
            var hasMore = slice.Count > pageSize;
            var items = slice.Take(pageSize)
                .Select(a => new TemplateStudioHistoryItem(a.Id, a.TaskDescription, a.ReasoningSummary, null, a.CostEur, a.CreatedAt))
                .ToList();
            return Task.FromResult(((IReadOnlyList<TemplateStudioHistoryItem>)items, hasMore));
        }
    }

    private sealed class EmptyProviderCatalog : IProviderCatalog
    {
        public IReadOnlyList<ProviderInfo> ListProviders() => [];
    }

    private sealed class ProviderWithUnavailableModel : IProviderCatalog
    {
        // Returns no matching provider name for what the profile expects
        public IReadOnlyList<ProviderInfo> ListProviders() => [new ProviderInfo("openrouter", "OpenRouter")];
    }

    private sealed class NoopModelCatalog : IModelCatalog
    {
        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(string providerName, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<ModelInfo>)[]);
        public Task<IReadOnlyList<ModelInfo>> RefreshAsync(string providerName, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<ModelInfo>)[]);
        public bool IsUsingFallback(string providerName) => false;
    }

    private sealed class NoopPricingCatalog : IPricingCatalog
    {
        public decimal? CalculateCostEur(string modelName, int inputTokens, int outputTokens, string? providerName = null, int cachedInputTokens = 0) => null;
    }

    private sealed class NoopEmbeddingProvider : IEmbeddingProvider
    {
        public string ProviderName => "test";
        public string ModelName => "test-model";
        public int Dimensions => 3;
        public Task<EmbeddingResult> CreateAsync(string text, CancellationToken ct)
            => Task.FromResult(new EmbeddingResult([1f, 0f, 0f], 1, null));
        public Task<IReadOnlyList<EmbeddingResult>> CreateBatchAsync(IReadOnlyList<string> texts, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class AlwaysStopLlmClient : ILlmClient
    {
        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
            => Task.FromResult(new LlmResponse
            {
                Text = "",
                FinishReason = "stop",
                TokenUsage = new LlmTokenUsage { InputTokens = 1, OutputTokens = 1 }
            });
    }

    private sealed class NoopAtomicTransactionFactory : IAtomicTransactionFactory
    {
        public Task<IAtomicTransaction> BeginAsync(CancellationToken ct = default)
            => Task.FromResult<IAtomicTransaction>(new NoopTransaction());

        private sealed class NoopTransaction : IAtomicTransaction
        {
            public Task CommitAsync(CancellationToken ct = default)  => Task.CompletedTask;
            public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static TemplateStudioService CreateService(
        ICrewService crewService,
        ITemplateStudioAnalysisRepository repo,
        IProviderCatalog? providerCatalog = null)
    {
        var resolver = new TestLlmClientResolver(new AlwaysStopLlmClient(), "anthropic/claude-sonnet-4-5", 4096);
        var embeddingProvider = new NoopEmbeddingProvider();
        var similarityService = new ProfileSimilarityService(crewService, embeddingProvider);
        var opts = Options.Create(new TemplateStudioOptions());

        return new TemplateStudioService(
            resolver,
            crewService,
            providerCatalog ?? new EmptyProviderCatalog(),
            new NoopModelCatalog(),
            new NoopPricingCatalog(),
            repo,
            similarityService,
            new NoopAtomicTransactionFactory(),
            new FakeStudioSettingsService(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TemplateStudioService>.Instance,
            opts);
    }

    private static ProposedTemplate MakeTemplate(string name = "new-template") => new(
        Name: name,
        DisplayName: "New Template",
        Description: "A new template for testing.",
        ExecutorProfileName: "default-executor",
        ReviewerProfileNames: ["briefing-fidelity"],
        AdvisorProfileNames: [],
        GroundingProviderProfileNames: [],
        EvaluationStrategy: "Sequential");

    private static ProposedProfile MakeReviewerProfile(string name = "custom-quality-reviewer") => new(
        ProfileType: ProposedProfileType.Reviewer,
        Name: name,
        DisplayName: "Quality Reviewer",
        Description: "Checks content quality.",
        Model: "gpt-4o-mini",
        Provider: "openrouter",
        SystemPrompt: "Review content quality.",
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
    public async Task MaterializeAsync_CreatesReviewerProfile_WhenProposedProfileTypeIsReviewer()
    {
        var crewService = new CapturingCrewService();
        var repo = new InMemoryAnalysisRepository();
        var svc = CreateService(crewService, repo);

        var analysisId = Guid.NewGuid();
        var request = new MaterializationRequest(
            FinalTemplate: MakeTemplate(),
            FinalNewProfiles: [MakeReviewerProfile()]);

        var result = await svc.MaterializeAsync(analysisId, request, CancellationToken.None);

        Assert.Single(crewService.CreatedReviewers);
        Assert.Single(result.CreatedProfileNames);
    }

    [Fact]
    public async Task MaterializeAsync_MarksAnalysisAsMaterialized()
    {
        var crewService = new CapturingCrewService();
        var repo = new InMemoryAnalysisRepository();
        var svc = CreateService(crewService, repo);

        var analysisId = Guid.NewGuid();
        var request = new MaterializationRequest(
            FinalTemplate: MakeTemplate("legal-template"),
            FinalNewProfiles: []);

        await svc.MaterializeAsync(analysisId, request, CancellationToken.None);

        Assert.True(repo.Materialized.ContainsKey(analysisId),
            "MarkMaterializedAsync should have been called with the correct analysisId.");
    }

    [Fact]
    public async Task MaterializeAsync_ThrowsWhenSystemProfileNameProposed()
    {
        var crewService = new CapturingCrewService();
        var repo = new InMemoryAnalysisRepository();
        var svc = CreateService(crewService, repo);

        // "briefing-fidelity" is a system profile name
        var systemNamedProfile = MakeReviewerProfile("briefing-fidelity");
        var request = new MaterializationRequest(
            FinalTemplate: MakeTemplate(),
            FinalNewProfiles: [systemNamedProfile]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.MaterializeAsync(Guid.NewGuid(), request, CancellationToken.None));
    }

    [Fact]
    public async Task MaterializeAsync_ResolvesTemplateProfleNamesToCustomPrefixedNames()
    {
        // Regression: newly created profiles get auto-prefixed with "custom-" by CrewService.
        // The template's profile name lists must be updated to use those final names,
        // otherwise ResolveSnapshotAsync fails when starting a run.
        var crewService = new CapturingCrewService();
        var repo = new InMemoryAnalysisRepository();
        var svc = CreateService(crewService, repo);

        var template = new ProposedTemplate(
            Name: "my-template",
            DisplayName: "My Template",
            Description: "Test",
            ExecutorProfileName: "default-executor",
            ReviewerProfileNames: ["my-reviewer"],   // proposed without "custom-" prefix
            AdvisorProfileNames: ["my-advisor"],      // proposed without "custom-" prefix
            GroundingProviderProfileNames: [],
            EvaluationStrategy: "Sequential");

        var reviewerProfile = MakeReviewerProfile("my-reviewer");
        var advisorProfile = new ProposedProfile(
            ProposedProfileType.Advisor, "my-advisor", "My Advisor", "Advises.", "gpt-4o-mini",
            "openrouter", "Be strategic.", null, null, "Strategic", "BeforeFirstExecution", null, null);

        var request = new MaterializationRequest(template, [reviewerProfile, advisorProfile]);
        await svc.MaterializeAsync(Guid.NewGuid(), request, CancellationToken.None);

        var stored = Assert.Single(crewService.CreatedTemplates);
        // Template must reference the final prefixed names, not the original proposal names.
        Assert.Contains("custom-my-reviewer", stored.ReviewerProfileNames);
        Assert.DoesNotContain("my-reviewer", stored.ReviewerProfileNames);
        Assert.Contains("custom-my-advisor", stored.AdvisorProfileNames);
        Assert.DoesNotContain("my-advisor", stored.AdvisorProfileNames);
    }

    [Fact]
    public async Task MaterializeAsync_ReturnsWarningsForUnavailableProviders()
    {
        var crewService = new CapturingCrewService();
        var repo = new InMemoryAnalysisRepository();

        // Provider catalog has "openrouter" but the profile references "unknown-provider"
        var profileWithUnknownProvider = new ProposedProfile(
            ProfileType: ProposedProfileType.GroundingProvider,
            Name: "custom-web-search",
            DisplayName: "Custom Web Search",
            Description: "Searches the web.",
            Model: "n/a",
            Provider: "unknown-provider",
            SystemPrompt: "Search.",
            MaxTokens: null,
            ReviewerFocus: null,
            AdvisorMode: null,
            AdvisorTrigger: null,
            GroundingProviderType: "tavily",
            GroundingProviderSettings: null);

        var svc = CreateService(crewService, repo, providerCatalog: new ProviderWithUnavailableModel());

        var request = new MaterializationRequest(
            FinalTemplate: MakeTemplate(),
            FinalNewProfiles: [profileWithUnknownProvider]);

        var result = await svc.MaterializeAsync(Guid.NewGuid(), request, CancellationToken.None);

        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("unknown-provider"));
    }
}
