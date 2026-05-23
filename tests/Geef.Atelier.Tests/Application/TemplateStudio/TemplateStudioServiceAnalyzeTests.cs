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

public sealed class TemplateStudioServiceAnalyzeTests
{
    // -------------------------------------------------------------------------
    // Test doubles
    // -------------------------------------------------------------------------

    private sealed class EmptyCrewService : ICrewService
    {
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

        public Task<ReviewerProfile> CreateCustomReviewerProfileAsync(ReviewerProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task<ReviewerProfile> UpdateCustomReviewerProfileAsync(ReviewerProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task DeleteCustomReviewerProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;

        public Task<ExecutorProfile> CreateCustomExecutorProfileAsync(ExecutorProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task<ExecutorProfile> UpdateCustomExecutorProfileAsync(ExecutorProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task DeleteCustomExecutorProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;

        public Task<AdvisorProfile> CreateCustomAdvisorProfileAsync(AdvisorProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task<AdvisorProfile> UpdateCustomAdvisorProfileAsync(AdvisorProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task DeleteCustomAdvisorProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;

        public Task<GroundingProviderProfile> CreateCustomGroundingProviderProfileAsync(GroundingProviderProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task<GroundingProviderProfile> UpdateCustomGroundingProviderProfileAsync(GroundingProviderProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task DeleteCustomGroundingProviderProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;

        public Task<CrewTemplate> CreateCustomCrewTemplateAsync(CrewTemplate template, CancellationToken ct = default) => Task.FromResult(template);
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

    private sealed class EmptyProviderCatalog : IProviderCatalog
    {
        public IReadOnlyList<ProviderInfo> ListProviders() => [];
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
        public decimal? CalculateCostEur(string modelName, int inputTokens, int outputTokens, string? providerName = null) => null;
    }

    private sealed class InMemoryAnalysisRepository : ITemplateStudioAnalysisRepository
    {
        private readonly List<TemplateStudioAnalysis> _store = [];
        private readonly Dictionary<Guid, string> _materialized = [];

        public Task CreateAsync(TemplateStudioAnalysis analysis, CancellationToken ct = default)
        {
            _store.Add(analysis);
            return Task.CompletedTask;
        }

        public Task<TemplateStudioAnalysis?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_store.FirstOrDefault(a => a.Id == id));

        public Task MarkMaterializedAsync(Guid analysisId, string templateName, CancellationToken ct = default)
        {
            _materialized[analysisId] = templateName;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TemplateStudioAnalysis>> ListRecentAsync(int limit = 10, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<TemplateStudioAnalysis>)_store.OrderByDescending(a => a.CreatedAt).Take(limit).ToList());

        public Task<(IReadOnlyList<TemplateStudioHistoryItem> Items, bool HasMore)> ListHistoryAsync(int page, int pageSize, CancellationToken ct = default)
        {
            var skip = page * pageSize;
            var take = pageSize + 1;
            var slice = _store.OrderByDescending(a => a.CreatedAt).Skip(skip).Take(take).ToList();
            var hasMore = slice.Count > pageSize;
            var items = slice.Take(pageSize)
                .Select(a => new TemplateStudioHistoryItem(a.Id, a.TaskDescription, a.ReasoningSummary, null, a.CostEur, a.CreatedAt))
                .ToList();
            return Task.FromResult(((IReadOnlyList<TemplateStudioHistoryItem>)items, hasMore));
        }

        public bool WasCreated(Guid id) => _store.Any(a => a.Id == id);
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

    // -------------------------------------------------------------------------
    // Fake LLM client returning specific tool-call responses
    // -------------------------------------------------------------------------

    private sealed class ToolCallFakeLlmClient(string toolArgumentsJson) : ILlmClient
    {
        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
            => Task.FromResult(new LlmResponse
            {
                Text = "",
                FinishReason = "tool_calls",
                ToolName = TemplateProposalTool.ToolName,
                ToolArgumentsJson = toolArgumentsJson,
                TokenUsage = new LlmTokenUsage { InputTokens = 100, OutputTokens = 50 }
            });
    }

    private sealed class NoToolCallFakeLlmClient : ILlmClient
    {
        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
            => Task.FromResult(new LlmResponse
            {
                Text = "I cannot help with that.",
                FinishReason = "stop",
                ToolName = null,
                ToolArgumentsJson = null,
                TokenUsage = new LlmTokenUsage { InputTokens = 20, OutputTokens = 10 }
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
    // Factory helpers
    // -------------------------------------------------------------------------

    private static TemplateStudioService CreateService(
        ILlmClient llmClient,
        ITemplateStudioAnalysisRepository? repo = null,
        ICrewService? crewService = null,
        IStudioSettingsService? studioSettings = null,
        TemplateStudioOptions? optionsValue = null)
    {
        var resolver = new TestLlmClientResolver(llmClient, "anthropic/claude-sonnet-4-5", 4096);
        var crew = crewService ?? new EmptyCrewService();
        var repository = repo ?? new InMemoryAnalysisRepository();
        var embeddingProvider = new NoopEmbeddingProvider();
        var similarityService = new ProfileSimilarityService(crew, embeddingProvider);
        var opts = Options.Create(optionsValue ?? new TemplateStudioOptions
        {
            Provider = "openrouter",
            Model = "anthropic/claude-sonnet-4-5",
            MaxTokens = 4096,
            SimilarityThreshold = 0.85
        });

        return new TemplateStudioService(
            resolver,
            crew,
            new EmptyProviderCatalog(),
            new NoopModelCatalog(),
            new NoopPricingCatalog(),
            repository,
            similarityService,
            new NoopAtomicTransactionFactory(),
            studioSettings ?? new FakeStudioSettingsService(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TemplateStudioService>.Instance,
            opts);
    }

    // Captures the model/maxTokens that the service actually requested from the LLM client.
    private sealed class CapturingLlmClient(string toolArgumentsJson) : ILlmClient
    {
        public string? CapturedModel { get; private set; }
        public int CapturedMaxTokens { get; private set; }

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            CapturedModel = request.Model;
            CapturedMaxTokens = request.MaxTokens;
            return Task.FromResult(new LlmResponse
            {
                Text = "",
                FinishReason = "tool_calls",
                ToolName = TemplateProposalTool.ToolName,
                ToolArgumentsJson = toolArgumentsJson,
                TokenUsage = new LlmTokenUsage { InputTokens = 100, OutputTokens = 50 }
            });
        }
    }

    private const string MinimalProposalJson = """
        {
            "matched_existing_templates": [],
            "recommendation": "create_new",
            "reasoning_summary": "Needs a new template."
        }
        """;

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AnalyzeAsync_WhenMetaLlmReturnsUseExisting_ReturnsCorrectRecommendation()
    {
        const string json = """
            {
                "matched_existing_templates": [
                    {"template_name": "klassik", "confidence": 0.95, "reasoning": "Perfect match."}
                ],
                "recommendation": "use_existing",
                "reasoning_summary": "The klassik template fits this task with 95% confidence."
            }
            """;

        var svc = CreateService(new ToolCallFakeLlmClient(json));
        var result = await svc.AnalyzeAsync("Write a short letter.", CancellationToken.None);

        Assert.Equal(StudioRecommendation.UseExistingTemplate, result.Recommendation);
        Assert.Single(result.MatchedExistingTemplates);
        Assert.Equal("klassik", result.MatchedExistingTemplates[0].TemplateName);
        Assert.Equal(0.95, result.MatchedExistingTemplates[0].Confidence);
        Assert.Null(result.ProposedTemplate);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenMetaLlmReturnsCreateNew_WithProposedTemplate_ReturnsTemplateProposal()
    {
        const string json = """
            {
                "matched_existing_templates": [],
                "recommendation": "create_new",
                "proposed_template": {
                    "name": "legal-review",
                    "display_name": "Legal Review",
                    "description": "Reviews legal contracts.",
                    "executor_profile_name": "default-executor",
                    "reviewer_profile_names": ["briefing-fidelity"],
                    "advisor_profile_names": [],
                    "grounding_provider_profile_names": [],
                    "evaluation_strategy": "Sequential"
                },
                "proposed_new_profiles": [],
                "reasoning_summary": "No existing template matches. Created a new legal review template."
            }
            """;

        var svc = CreateService(new ToolCallFakeLlmClient(json));
        var result = await svc.AnalyzeAsync("Review legal contracts for risks.", CancellationToken.None);

        Assert.Equal(StudioRecommendation.CreateNewTemplate, result.Recommendation);
        Assert.NotNull(result.ProposedTemplate);
        Assert.Equal("legal-review", result.ProposedTemplate!.Name);
        Assert.Equal("Legal Review", result.ProposedTemplate.DisplayName);
        Assert.Empty(result.ProposedNewProfiles);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenMetaLlmDoesNotCallTool_ThrowsInvalidOperationException()
    {
        var svc = CreateService(new NoToolCallFakeLlmClient());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.AnalyzeAsync("Some task description.", CancellationToken.None));
    }

    [Fact]
    public async Task AnalyzeAsync_PersistsAnalysisRecord()
    {
        const string json = """
            {
                "matched_existing_templates": [],
                "recommendation": "create_new",
                "reasoning_summary": "Needs a new template."
            }
            """;

        var repo = new InMemoryAnalysisRepository();
        var svc = CreateService(new ToolCallFakeLlmClient(json), repo: repo);

        var result = await svc.AnalyzeAsync("Complex task.", CancellationToken.None);

        Assert.True(repo.WasCreated(result.Id));
    }

    // -------------------------------------------------------------------------
    // Model resolution: override → persisted default → appsettings default
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AnalyzeAsync_WithExplicitOverride_UsesOverrideModelAndMaxTokens()
    {
        var client = new CapturingLlmClient(MinimalProposalJson);
        var svc = CreateService(client, studioSettings: new FakeStudioSettingsService());

        await svc.AnalyzeAsync("Task.", new StudioModelChoice("codex-cli", "gpt-5.5", 20000), CancellationToken.None);

        Assert.Equal("gpt-5.5", client.CapturedModel);
        Assert.Equal(20000, client.CapturedMaxTokens);
    }

    [Fact]
    public async Task AnalyzeAsync_WithoutOverride_UsesPersistedDefault()
    {
        var client = new CapturingLlmClient(MinimalProposalJson);
        var persisted = new FakeStudioSettingsService(new Geef.Atelier.Core.Domain.StudioSettings
        {
            Provider = "claude-cli", Model = "claude-haiku-4.5", MaxTokens = 12000
        });
        var svc = CreateService(client, studioSettings: persisted);

        await svc.AnalyzeAsync("Task.", CancellationToken.None);

        Assert.Equal("claude-haiku-4.5", client.CapturedModel);
        Assert.Equal(12000, client.CapturedMaxTokens);
    }

    [Fact]
    public async Task AnalyzeAsync_WithoutOverrideAndNoPersisted_FallsBackToAppsettings()
    {
        var client = new CapturingLlmClient(MinimalProposalJson);
        var svc = CreateService(client, studioSettings: new FakeStudioSettingsService());

        await svc.AnalyzeAsync("Task.", CancellationToken.None);

        Assert.Equal("anthropic/claude-sonnet-4-5", client.CapturedModel);
        Assert.Equal(4096, client.CapturedMaxTokens);
    }

    [Fact]
    public async Task AnalyzeAsync_WithIncompleteOverride_FallsBackToPersisted()
    {
        var client = new CapturingLlmClient(MinimalProposalJson);
        var persisted = new FakeStudioSettingsService(new Geef.Atelier.Core.Domain.StudioSettings
        {
            Provider = "claude-cli", Model = "claude-haiku-4.5", MaxTokens = 12000
        });
        var svc = CreateService(client, studioSettings: persisted);

        // Provider present but model empty → incomplete → must not be used as override.
        await svc.AnalyzeAsync("Task.", new StudioModelChoice("codex-cli", "", 0), CancellationToken.None);

        Assert.Equal("claude-haiku-4.5", client.CapturedModel);
        Assert.Equal(12000, client.CapturedMaxTokens);
    }

    [Fact]
    public async Task GetEffectiveDefaultAsync_NoPersisted_ReturnsAppsettings()
    {
        var svc = CreateService(new ToolCallFakeLlmClient(MinimalProposalJson),
            studioSettings: new FakeStudioSettingsService());

        var choice = await svc.GetEffectiveDefaultAsync(CancellationToken.None);

        Assert.Equal("openrouter", choice.Provider);
        Assert.Equal("anthropic/claude-sonnet-4-5", choice.Model);
        Assert.Equal(4096, choice.MaxTokens);
    }

    [Fact]
    public async Task SaveDefaultAsync_PersistsChoice()
    {
        var store = new FakeStudioSettingsService();
        var svc = CreateService(new ToolCallFakeLlmClient(MinimalProposalJson), studioSettings: store);

        await svc.SaveDefaultAsync(new StudioModelChoice("codex-cli", "gpt-5.5", 20000), CancellationToken.None);

        Assert.Equal("codex-cli", store.Current.Provider);
        Assert.Equal("gpt-5.5", store.Current.Model);
        Assert.Equal(20000, store.Current.MaxTokens);
    }

    [Fact]
    public async Task SaveDefaultAsync_WithEmptyChoice_FallsBackToAppsettings()
    {
        var store = new FakeStudioSettingsService();
        var svc = CreateService(new ToolCallFakeLlmClient(MinimalProposalJson), studioSettings: store);

        await svc.SaveDefaultAsync(new StudioModelChoice("", "", 0), CancellationToken.None);

        Assert.Equal("openrouter", store.Current.Provider);
        Assert.Equal("anthropic/claude-sonnet-4-5", store.Current.Model);
        Assert.Equal(4096, store.Current.MaxTokens);
    }
}
