using Geef.Atelier.Application.Composition;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Core.Persistence.Tools;
using Geef.Atelier.Infrastructure.Composition;
using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Tests.Composition;

/// <summary>
/// Tests for the composer stability fixes:
/// - Validator skips model check for deterministic finalizer types.
/// - PreferredComposerModels provides valid, plural model choices.
/// </summary>
public sealed class CrewComposerStabilityTests
{
    // ── Helpers ───────────────────────────────────────────────────────────

    private static CrewSpecValidator MakeValidator(
        ICrewService? crewService = null,
        IModelCatalog? modelCatalog = null,
        IGroundingProviderFactory? groundingFactory = null)
    {
        crewService  ??= new EmptyCrewService();
        modelCatalog ??= new EmptyModelCatalog();
        groundingFactory ??= new StubGroundingFactory("tavily", "academic-search", "vector-store");
        return new CrewSpecValidator(crewService, modelCatalog, groundingFactory,
            new EmptyToolRepository(), new AgenticCapableResolver());
    }

    private static CrewSpecValidator MakeValidatorWithKnownModel(string provider, string model) =>
        MakeValidator(modelCatalog: new SingleModelCatalog(provider, model));

    private static CrewSpecValidator MakeValidatorWithNoModels() =>
        MakeValidator(modelCatalog: new EmptyModelCatalog());

    // ── Deterministic finalizer: no model required ────────────────────────

    [Theory]
    [InlineData("learning-extractor")]
    [InlineData("file-export")]
    [InlineData("metadata-enrich")]
    [InlineData("external-sink")]
    [InlineData("crew-materialize")]
    [InlineData("learning-publisher")]
    public async Task ValidateAsync_NoModelIssue_WhenDeterministicFinalizerHasNoModel(string finalizerType)
    {
        // An inline finalizer with a deterministic type and no provider/model must not produce
        // a model-validation issue, even when the model catalog is empty.
        var spec = $$"""
            {
                "mode": "composed",
                "executor": {"reuse": "default-executor"},
                "reviewers": [{"reuse": "briefing-fidelity"}],
                "finalizers": [{
                    "name": "test-finalizer",
                    "finalizer_type": "{{finalizerType}}"
                }]
            }
            """;

        var validator = MakeValidatorWithNoModels();
        var issues = await validator.ValidateAsync(spec);

        var modelIssues = issues.Where(i => i.Field.StartsWith("finalizers[") && i.Field.EndsWith(".model")).ToList();
        Assert.Empty(modelIssues);
    }

    [Fact]
    public async Task ValidateAsync_FlagsModelIssue_WhenTransformFinalizerHasInvalidModel()
    {
        // Transform finalizers ARE LLM-based and must be validated.
        const string spec = """
            {
                "mode": "composed",
                "executor": {"reuse": "default-executor"},
                "reviewers": [{"reuse": "briefing-fidelity"}],
                "finalizers": [{
                    "name": "tone-shift",
                    "finalizer_type": "Transform",
                    "provider": "claude-cli",
                    "model": "nonexistent-model"
                }]
            }
            """;

        var validator = MakeValidatorWithNoModels();
        var issues = await validator.ValidateAsync(spec);

        Assert.Contains(issues, i => i.Field == "finalizers[0].model");
    }

    [Fact]
    public async Task ValidateAsync_NoModelIssue_WhenTransformFinalizerHasValidModel()
    {
        const string spec = """
            {
                "mode": "composed",
                "executor": {"reuse": "default-executor"},
                "reviewers": [{"reuse": "briefing-fidelity"}],
                "finalizers": [{
                    "name": "tone-shift",
                    "finalizer_type": "Transform",
                    "provider": "claude-cli",
                    "model": "claude-opus-4-8"
                }]
            }
            """;

        var validator = MakeValidatorWithKnownModel("claude-cli", "claude-opus-4-8");
        var issues = await validator.ValidateAsync(spec);

        Assert.DoesNotContain(issues, i => i.Field == "finalizers[0].model");
    }

    [Fact]
    public async Task ValidateAsync_NoFinalizerModelIssue_WhenLearningExtractorReusedAsOutputFinalizer()
    {
        // The learning-extractor reuse is a valid output finalizer.
        // The validator must not produce any finalizer-related model or reuse-not-found issues for it.
        const string spec = """
            {
                "mode": "composed",
                "executor": {"reuse": "default-executor"},
                "reviewers": [{"reuse": "briefing-fidelity"}],
                "finalizers": [{"reuse": "learning-extractor"}]
            }
            """;

        var crewService = new LearningExtractorCrewService();
        var validator   = MakeValidator(crewService);
        var issues      = await validator.ValidateAsync(spec);

        // No finalizer-related issues.
        var finalizerIssues = issues.Where(i => i.Field.StartsWith("finalizer")).ToList();
        Assert.Empty(finalizerIssues);
    }

    // ── PreferredComposerModels ───────────────────────────────────────────

    [Fact]
    public void PreferredComposerModels_ExecutorModel_DiffersFromAllReviewerModels()
    {
        // The executor model must differ from all preferred reviewer models (model plurality).
        var (execProvider, execModel) = PreferredComposerModels.Executor;

        foreach (var (revProvider, revModel) in PreferredComposerModels.Reviewers)
        {
            // Different model OR different provider is acceptable for plurality;
            // same provider+model combination is the violation.
            var isSameCombo = string.Equals(execProvider, revProvider, StringComparison.OrdinalIgnoreCase)
                           && string.Equals(execModel, revModel, StringComparison.OrdinalIgnoreCase);
            Assert.False(isSameCombo,
                $"Reviewer ({revProvider}/{revModel}) is the same as the executor ({execProvider}/{execModel}). Model plurality violated.");
        }
    }

    [Fact]
    public void PreferredComposerModels_HasAtLeastTwoReviewers()
    {
        Assert.True(PreferredComposerModels.Reviewers.Length >= 2,
            "At least two preferred reviewer models are required for meaningful plurality.");
    }

    [Fact]
    public void PreferredComposerModels_NoLegacyModelIds()
    {
        // These legacy model IDs must never appear in the curated shortlist.
        string[] forbiddenFragments = ["4-7", "opus-4-7", "gemini-2.5", "gpt-4o", "claude-sonnet-4\"", "claude-opus-4\""];

        foreach (var (provider, model) in PreferredComposerModels.All)
        {
            foreach (var fragment in forbiddenFragments)
            {
                Assert.DoesNotContain(fragment, model, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void PreferredComposerModels_AllHaveNonEmptyProviderAndModel()
    {
        foreach (var (provider, model) in PreferredComposerModels.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(provider), "Provider must not be empty.");
            Assert.False(string.IsNullOrWhiteSpace(model), "Model must not be empty.");
        }
    }

    // ── Stubs ─────────────────────────────────────────────────────────────

    private sealed class EmptyModelCatalog : IModelCatalog
    {
        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(string p, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ModelInfo>>([]);
        public Task<IReadOnlyList<ModelInfo>> RefreshAsync(string p, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ModelInfo>>([]);
        public bool IsUsingFallback(string p) => false;
    }

    private sealed class StubGroundingFactory(params string[] types) : IGroundingProviderFactory
    {
        private readonly HashSet<string> _types = new(types, StringComparer.OrdinalIgnoreCase);
        public IGroundingProvider Create(string providerType) => throw new NotSupportedException();
        public bool IsRegistered(string providerType) => _types.Contains(providerType);
        public IReadOnlyCollection<string> RegisteredTypes => _types;
    }

    private sealed class SingleModelCatalog(string provider, string model) : IModelCatalog
    {
        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(string p, CancellationToken ct = default)
        {
            IReadOnlyList<ModelInfo> list = string.Equals(p, provider, StringComparison.OrdinalIgnoreCase)
                ? [new ModelInfo(model, model, null, true)]
                : [];
            return Task.FromResult(list);
        }
        public Task<IReadOnlyList<ModelInfo>> RefreshAsync(string p, CancellationToken ct = default) => ListModelsAsync(p, ct);
        public bool IsUsingFallback(string p) => false;
    }

    private sealed class EmptyCrewService : ICrewService
    {
        public Task<CrewTemplate?> GetCrewTemplateAsync(string name, CancellationToken ct = default) => Task.FromResult<CrewTemplate?>(null);
        public Task<ExecutorProfile?> GetExecutorProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<ExecutorProfile?>(null);
        public Task<ReviewerProfile?> GetReviewerProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<ReviewerProfile?>(null);
        public Task<FinalizerProfile?> GetFinalizerProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<FinalizerProfile?>(null);
        public Task<AdvisorProfile?> GetAdvisorProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<AdvisorProfile?>(null);
        public Task<GroundingProviderProfile?> GetGroundingProviderProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<GroundingProviderProfile?>(null);
        public Task<IReadOnlyList<ReviewerProfile>> ListReviewerProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ReviewerProfile>>([]);
        public Task<ReviewerProfile> CreateCustomReviewerProfileAsync(ReviewerProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<ReviewerProfile> UpdateCustomReviewerProfileAsync(ReviewerProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomReviewerProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomReviewerProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<ExecutorProfile>> ListExecutorProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ExecutorProfile>>([]);
        public Task<ExecutorProfile> CreateCustomExecutorProfileAsync(ExecutorProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<ExecutorProfile> UpdateCustomExecutorProfileAsync(ExecutorProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomExecutorProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomExecutorProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<AdvisorProfile>> ListAdvisorProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AdvisorProfile>>([]);
        public Task<AdvisorProfile> CreateCustomAdvisorProfileAsync(AdvisorProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<AdvisorProfile> UpdateCustomAdvisorProfileAsync(AdvisorProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomAdvisorProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomAdvisorProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<GroundingProviderProfile>> ListGroundingProviderProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GroundingProviderProfile>>([]);
        public Task<GroundingProviderProfile> CreateCustomGroundingProviderProfileAsync(GroundingProviderProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<GroundingProviderProfile> UpdateCustomGroundingProviderProfileAsync(GroundingProviderProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomGroundingProviderProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomGroundingProviderProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<FinalizerProfile>> ListFinalizerProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<FinalizerProfile>>([]);
        public Task<FinalizerProfile> CreateCustomFinalizerProfileAsync(FinalizerProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<FinalizerProfile> UpdateCustomFinalizerProfileAsync(FinalizerProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomFinalizerProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomFinalizerProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<CrewTemplate>> ListCrewTemplatesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CrewTemplate>>([]);
        public Task<CrewTemplate> CreateCustomCrewTemplateAsync(CrewTemplate t, CancellationToken ct = default) => Task.FromResult(t);
        public Task<CrewTemplate> UpdateCustomCrewTemplateAsync(CrewTemplate t, CancellationToken ct = default) => Task.FromResult(t);
        public Task DeleteCustomCrewTemplateAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomCrewTemplateAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<CrewSnapshot> ResolveSnapshotAsync(string? crewTemplateName, CrewSpec? customCrew, CancellationToken ct = default)
            => Task.FromResult(new CrewSnapshot(1, crewTemplateName, null!, [], EvaluationStrategy.Parallel, null, []));
    }

    /// <summary>Returns a valid <c>learning-extractor</c> finalizer profile for reuse lookup.</summary>
    private sealed class LearningExtractorCrewService : ICrewService
    {
        private static readonly FinalizerProfile LearningExtractor = new(
            Name: "learning-extractor", DisplayName: "Learning Extractor",
            Description: "Extracts learnings.", FinalizerType: FinalizerType.LearningExtract,
            Settings: new Dictionary<string, string>(), IsSystem: true);

        public Task<CrewTemplate?> GetCrewTemplateAsync(string name, CancellationToken ct = default) => Task.FromResult<CrewTemplate?>(null);
        public Task<ExecutorProfile?> GetExecutorProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<ExecutorProfile?>(null);
        public Task<ReviewerProfile?> GetReviewerProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<ReviewerProfile?>(null);
        public Task<FinalizerProfile?> GetFinalizerProfileAsync(string name, CancellationToken ct = default) =>
            Task.FromResult(name == "learning-extractor" ? (FinalizerProfile?)LearningExtractor : null);
        public Task<AdvisorProfile?> GetAdvisorProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<AdvisorProfile?>(null);
        public Task<GroundingProviderProfile?> GetGroundingProviderProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<GroundingProviderProfile?>(null);
        public Task<IReadOnlyList<ReviewerProfile>> ListReviewerProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ReviewerProfile>>([]);
        public Task<ReviewerProfile> CreateCustomReviewerProfileAsync(ReviewerProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<ReviewerProfile> UpdateCustomReviewerProfileAsync(ReviewerProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomReviewerProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomReviewerProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<ExecutorProfile>> ListExecutorProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ExecutorProfile>>([]);
        public Task<ExecutorProfile> CreateCustomExecutorProfileAsync(ExecutorProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<ExecutorProfile> UpdateCustomExecutorProfileAsync(ExecutorProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomExecutorProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomExecutorProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<AdvisorProfile>> ListAdvisorProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AdvisorProfile>>([]);
        public Task<AdvisorProfile> CreateCustomAdvisorProfileAsync(AdvisorProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<AdvisorProfile> UpdateCustomAdvisorProfileAsync(AdvisorProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomAdvisorProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomAdvisorProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<GroundingProviderProfile>> ListGroundingProviderProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GroundingProviderProfile>>([]);
        public Task<GroundingProviderProfile> CreateCustomGroundingProviderProfileAsync(GroundingProviderProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<GroundingProviderProfile> UpdateCustomGroundingProviderProfileAsync(GroundingProviderProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomGroundingProviderProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomGroundingProviderProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<FinalizerProfile>> ListFinalizerProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<FinalizerProfile>>([]);
        public Task<FinalizerProfile> CreateCustomFinalizerProfileAsync(FinalizerProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<FinalizerProfile> UpdateCustomFinalizerProfileAsync(FinalizerProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomFinalizerProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomFinalizerProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<CrewTemplate>> ListCrewTemplatesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CrewTemplate>>([]);
        public Task<CrewTemplate> CreateCustomCrewTemplateAsync(CrewTemplate t, CancellationToken ct = default) => Task.FromResult(t);
        public Task<CrewTemplate> UpdateCustomCrewTemplateAsync(CrewTemplate t, CancellationToken ct = default) => Task.FromResult(t);
        public Task DeleteCustomCrewTemplateAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomCrewTemplateAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<CrewSnapshot> ResolveSnapshotAsync(string? crewTemplateName, CrewSpec? customCrew, CancellationToken ct = default)
            => Task.FromResult(new CrewSnapshot(1, crewTemplateName, null!, [], EvaluationStrategy.Parallel, null, []));
    }

    /// <summary>Returns null for every tool name lookup.</summary>
    private sealed class EmptyToolRepository : IToolDefinitionRepository
    {
        public Task<ToolDefinition?> GetByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult<ToolDefinition?>(null);
        public Task<IReadOnlyList<ToolDefinition>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ToolDefinition>>([]);
        public Task<IReadOnlyList<ToolDefinition>> GetSystemToolsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ToolDefinition>>([]);
        public Task<IReadOnlyList<ToolDefinition>> GetCustomToolsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ToolDefinition>>([]);
        public Task UpsertAsync(ToolDefinition tool, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>Reports every provider as supporting agentic tools.</summary>
    private sealed class AgenticCapableResolver : ILlmClientResolver
    {
        public (ILlmClient Client, string Model, int MaxTokens) ForActor(string actorName)
            => throw new NotSupportedException();
        public (ILlmClient Client, string Model, int MaxTokens) ForProfile(string provider, string model, int? maxTokens)
            => throw new NotSupportedException();
        public bool SupportsAgenticTools(string providerName) => true;
    }
}
