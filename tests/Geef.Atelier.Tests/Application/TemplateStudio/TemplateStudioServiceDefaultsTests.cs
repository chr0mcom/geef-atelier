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

/// <summary>Tests that service-level default-filling, executor support, and reasoning fields work correctly.</summary>
public sealed class TemplateStudioServiceDefaultsTests
{
    // -------------------------------------------------------------------------
    // Test doubles (trimmed variants of the Analyze test doubles)
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
        public Task CreateAsync(TemplateStudioAnalysis analysis, CancellationToken ct = default)
        {
            _store.Add(analysis);
            return Task.CompletedTask;
        }
        public Task<TemplateStudioAnalysis?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_store.FirstOrDefault(a => a.Id == id));
        public Task MarkMaterializedAsync(Guid analysisId, string templateName, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<IReadOnlyList<TemplateStudioAnalysis>> ListRecentAsync(int limit = 10, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<TemplateStudioAnalysis>)_store.Take(limit).ToList());
        public Task<(IReadOnlyList<TemplateStudioHistoryItem> Items, bool HasMore)> ListHistoryAsync(int page, int pageSize, CancellationToken ct = default)
            => Task.FromResult(((IReadOnlyList<TemplateStudioHistoryItem>)[], false));
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

    private sealed class NoopAtomicTransactionFactory : IAtomicTransactionFactory
    {
        public Task<IAtomicTransaction> BeginAsync(CancellationToken ct = default)
            => Task.FromResult<IAtomicTransaction>(new NoopTransaction());
        private sealed class NoopTransaction : IAtomicTransaction
        {
            public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

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

    // -------------------------------------------------------------------------
    // Factory helpers
    // -------------------------------------------------------------------------

    private static TemplateStudioService CreateService(ILlmClient llmClient, StudioDefaults? defaults = null)
    {
        var resolver = new TestLlmClientResolver(llmClient, "anthropic/claude-sonnet-4-5", 4096);
        var crew = new EmptyCrewService();
        var repo = new InMemoryAnalysisRepository();
        var embeddingProvider = new NoopEmbeddingProvider();
        var similarityService = new ProfileSimilarityService(crew, embeddingProvider);
        var opts = Options.Create(new TemplateStudioOptions
        {
            Model = "anthropic/claude-sonnet-4-5",
            MaxTokens = 4096,
            SimilarityThreshold = 2.0, // Disable deduplication — embedder returns same vector for all
            Defaults = defaults ?? new StudioDefaults()
        });

        return new TemplateStudioService(
            resolver, crew, new EmptyProviderCatalog(), new NoopModelCatalog(),
            new NoopPricingCatalog(), repo, similarityService,
            new NoopAtomicTransactionFactory(), new FakeStudioSettingsService(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TemplateStudioService>.Instance, opts);
    }

    // -------------------------------------------------------------------------
    // Tests: Defaults
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AnalyzeAsync_WhenProfileHasNoModel_AppliesReviewerDefault()
    {
        const string json = """
            {
                "matched_existing_templates": [],
                "recommendation": "create_new",
                "proposed_new_profiles": [
                    {
                        "profile_type": "reviewer",
                        "name": "custom-reviewer",
                        "display_name": "Custom Reviewer",
                        "description": "Checks content.",
                        "system_prompt": "Review thoroughly."
                    }
                ],
                "reasoning_summary": "Needs a new template."
            }
            """;

        var defaults = new StudioDefaults
        {
            ReviewerModel = "openai/gpt-4o-mini",
            ReviewerProvider = "openrouter"
        };
        var svc = CreateService(new ToolCallFakeLlmClient(json), defaults);
        var result = await svc.AnalyzeAsync("Review some content.", CancellationToken.None);

        Assert.Single(result.ProposedNewProfiles);
        var profile = result.ProposedNewProfiles[0];
        Assert.Equal("openai/gpt-4o-mini", profile.Model);
        Assert.Equal("openrouter", profile.Provider);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenProfileAlreadyHasModel_DoesNotOverrideWithDefault()
    {
        const string json = """
            {
                "matched_existing_templates": [],
                "recommendation": "create_new",
                "proposed_new_profiles": [
                    {
                        "profile_type": "reviewer",
                        "name": "custom-reviewer",
                        "display_name": "Custom Reviewer",
                        "description": "Checks content.",
                        "model": "anthropic/claude-haiku-4-5",
                        "provider": "anthropic",
                        "system_prompt": "Review thoroughly."
                    }
                ],
                "reasoning_summary": "LLM explicitly chose a model."
            }
            """;

        var defaults = new StudioDefaults { ReviewerModel = "openai/gpt-4o-mini" };
        var svc = CreateService(new ToolCallFakeLlmClient(json), defaults);
        var result = await svc.AnalyzeAsync("Review some content.", CancellationToken.None);

        Assert.Equal("anthropic/claude-haiku-4-5", result.ProposedNewProfiles[0].Model);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenExecutorProfileProposed_ParsesCorrectly()
    {
        const string json = """
            {
                "matched_existing_templates": [],
                "recommendation": "create_new",
                "proposed_new_profiles": [
                    {
                        "profile_type": "executor",
                        "name": "custom-executor",
                        "display_name": "Custom Executor",
                        "description": "Executes tasks.",
                        "model": "anthropic/claude-opus-4-8",
                        "provider": "anthropic",
                        "system_prompt": "Execute carefully."
                    }
                ],
                "reasoning_summary": "Custom executor needed."
            }
            """;

        var svc = CreateService(new ToolCallFakeLlmClient(json));
        var result = await svc.AnalyzeAsync("Execute a complex task.", CancellationToken.None);

        Assert.Single(result.ProposedNewProfiles);
        Assert.Equal(ProposedProfileType.Executor, result.ProposedNewProfiles[0].ProfileType);
        Assert.Equal("custom-executor", result.ProposedNewProfiles[0].Name);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenLlmProvidesReasoningFields_PreservesThemInResult()
    {
        const string json = """
            {
                "matched_existing_templates": [],
                "recommendation": "create_new",
                "proposed_template": {
                    "name": "smart-template",
                    "display_name": "Smart Template",
                    "description": "A smart template.",
                    "executor_profile_name": "default-executor",
                    "reviewer_profile_names": ["briefing-fidelity"],
                    "advisor_profile_names": [],
                    "grounding_provider_profile_names": [],
                    "evaluation_strategy": "Parallel",
                    "evaluation_strategy_reasoning": "Parallel is faster for this workload."
                },
                "proposed_new_profiles": [],
                "reasoning_summary": "LLM provided full reasoning."
            }
            """;

        var svc = CreateService(new ToolCallFakeLlmClient(json));
        var result = await svc.AnalyzeAsync("Smart task.", CancellationToken.None);

        Assert.NotNull(result.ProposedTemplate?.EvaluationStrategyReasoning);
        Assert.Equal("Parallel is faster for this workload.", result.ProposedTemplate!.EvaluationStrategyReasoning);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenProfileHasModelReasoning_PreservesIt()
    {
        const string json = """
            {
                "matched_existing_templates": [],
                "recommendation": "create_new",
                "proposed_new_profiles": [
                    {
                        "profile_type": "reviewer",
                        "name": "smart-reviewer",
                        "display_name": "Smart Reviewer",
                        "description": "Smart.",
                        "model": "openai/gpt-4o",
                        "provider": "openrouter",
                        "system_prompt": "Review.",
                        "model_reasoning": "GPT-4o has excellent reasoning for this domain."
                    }
                ],
                "reasoning_summary": "Profile with reasoning."
            }
            """;

        var svc = CreateService(new ToolCallFakeLlmClient(json));
        var result = await svc.AnalyzeAsync("Smart review task.", CancellationToken.None);

        var profile = result.ProposedNewProfiles[0];
        Assert.Equal("GPT-4o has excellent reasoning for this domain.", profile.ModelReasoning);
    }

    // -------------------------------------------------------------------------
    // Tests: Materialize — ValidateReviewerCount
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MaterializeAsync_WithZeroReviewers_ThrowsInvalidOperationException()
    {
        var svc = CreateService(new ToolCallFakeLlmClient("{}"));
        var templateWithNoReviewers = new ProposedTemplate(
            Name: "custom-no-reviewer",
            DisplayName: "No Reviewer Template",
            Description: "Has no reviewers.",
            ExecutorProfileName: "default-executor",
            ReviewerProfileNames: [],
            AdvisorProfileNames: [],
            GroundingProviderProfileNames: [],
            EvaluationStrategy: "Sequential");

        var request = new MaterializationRequest(templateWithNoReviewers, []);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.MaterializeAsync(Guid.NewGuid(), request, CancellationToken.None));
    }
}
