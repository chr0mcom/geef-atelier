using Geef.Atelier.Application.Composition;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Composition;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Infrastructure.Composition;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Composition;

/// <summary>D-066: the materializer persists model slots and matrix bindings instead of composer-chosen models.</summary>
public sealed class CrewMaterializerModelMatrixTests
{
    private const string Spec = """
        {
            "mode": "new",
            "domain": "german-pre-litigation-settlement-letter",
            "model_domain": "Legal",
            "rationale": "Legal letter",
            "executor": {"name": "letter-writer", "model_slot": "Executor", "provider": "openrouter", "model": "anthropic/claude-opus-5", "max_tokens": 48000, "system_prompt": "Write."},
            "reviewers": [
                {"name": "facts", "model_slot": "FactReviewer", "max_tokens": 8000, "system_prompt": "Check facts."},
                {"name": "strategy", "provider": "xai", "model": "grok-4.5", "max_tokens": 8000, "system_prompt": "Check strategy."}
            ],
            "advisors": [
                {"name": "devil", "advisor_mode": "DevilsAdvocate", "max_tokens": 8000, "system_prompt": "Challenge."}
            ],
            "grounding_providers": [
                {"name": "web", "provider_type": "tavily", "settings": {"Tier": "advanced", "refinementMode": "0", "refinementProvider": "deepseek", "refinementModel": "deepseek/deepseek-v4-flash"}},
                {"name": "plain", "provider_type": "tavily", "settings": {"Tier": "basic"}}
            ],
            "finalizers": [
                {"name": "polish", "finalizer_type": "Transform", "model_slot": "TransformRewrite", "system_prompt": "Remove AI voice.", "provider": "codex-cli", "model": "gpt-5.6-sol"},
                {"name": "summary", "finalizer_type": "Transform", "system_prompt": "Summarize."},
                {"reuse": "learning-extractor"}
            ]
        }
        """;

    [Fact]
    public async Task PersistsSlotsMatrixBindingsAndTheCrewDomain()
    {
        var crew = new CapturingCrewService();
        var result = await Materializer(crew).MaterializeAsync(Spec, Guid.NewGuid());

        Assert.False(result.WasDuplicate);
        Assert.Equal(ModelDomain.Legal, crew.Template!.Domain);

        var executor = Assert.Single(crew.Executors);
        Assert.Equal(ModelSlot.Executor, executor.ModelSlot);
        Assert.Null(executor.ModelDomain);
        Assert.Equal(("claude-cli", "claude-opus-latest"), (executor.Provider, executor.Model));
        Assert.Equal(48000, executor.MaxTokens);

        Assert.Equal(ModelSlot.FactReviewer, crew.Reviewers[0].ModelSlot);
        Assert.Equal(("codex-cli", "gpt-astra-latest"), (crew.Reviewers[0].Provider, crew.Reviewers[0].Model));
        Assert.Equal(ModelSlot.DomainReviewer, crew.Reviewers[1].ModelSlot);
        Assert.Equal(("openrouter", "google/gemini-4-neon"), (crew.Reviewers[1].Provider, crew.Reviewers[1].Model));

        var advisor = Assert.Single(crew.Advisors);
        Assert.Equal(AdvisorMode.DevilsAdvocate, advisor.Mode);
        Assert.Equal(ModelSlot.CriticalAdvisor, advisor.ModelSlot);

        Assert.Contains(result.Warnings, w => w.Contains("executor") && w.Contains("overridden by the model matrix"));
        Assert.Contains(result.Warnings, w => w.Contains("reviewers[1]") && w.Contains("xai/grok-4.5"));
    }

    [Fact]
    public async Task TransformFinalizersKeepPromptSlotAndBinding()
    {
        var crew = new CapturingCrewService();
        await Materializer(crew).MaterializeAsync(Spec, Guid.NewGuid());

        var polish = TransformSettings.From(crew.Finalizers.Single(f => f.Name == "polish").Settings);
        Assert.Equal("Remove AI voice.", polish.SystemPrompt);
        Assert.Equal(ModelSlot.TransformRewrite, polish.ModelSlot);
        Assert.Equal(("codex-cli", "gpt-sol-latest"), (polish.Provider, polish.Model));

        var summary = TransformSettings.From(crew.Finalizers.Single(f => f.Name == "summary").Settings);
        Assert.Equal("Summarize.", summary.SystemPrompt);
        Assert.Equal(ModelSlot.TransformCondense, summary.ModelSlot);
        Assert.Equal(("claude-cli", "claude-sonnet-latest"), (summary.Provider, summary.Model));
    }

    [Fact]
    public async Task GroundingSettingsAreKeptAndRefinementGetsTheMatrixRefiner()
    {
        var crew = new CapturingCrewService();
        await Materializer(crew).MaterializeAsync(Spec, Guid.NewGuid());

        var web = crew.Groundings.Single(g => g.Name == "web");
        Assert.Equal("advanced", web.ProviderSettings["Tier"]);
        Assert.Equal(ModelSlot.GroundingRefiner, web.RefinementModelSlot);
        Assert.Equal("claude-cli", web.ProviderSettings[GroundingProviderProfile.KeyRefinementProvider]);
        Assert.Equal("claude-sonnet-latest", web.ProviderSettings[GroundingProviderProfile.KeyRefinementModel]);
        Assert.Equal("2048", web.ProviderSettings[GroundingProviderProfile.KeyRefinementMaxTokens]);

        var plain = crew.Groundings.Single(g => g.Name == "plain");
        Assert.Null(plain.RefinementBinding);
        Assert.False(plain.ProviderSettings.ContainsKey(GroundingProviderProfile.KeyModelSlot));
    }

    [Fact]
    public async Task ComposerSettingsAreReducedToTheWhitelist()
    {
        var crew = new CapturingCrewService();
        var spec = Spec
            .Replace("""{"name": "plain", "provider_type": "tavily", "settings": {"Tier": "basic"}}""",
                """{"name": "plain", "provider_type": "tavily", "settings": {"MaxResults": "5", "apiKeyEnv": "CLI_PROXY_API_KEY", "url": "https://example.invalid"}}, {"name": "fetch", "provider_type": "url-fetch", "settings": {"urls": "http://169.254.169.254/latest/meta-data"}}""")
            .Replace("""{"name": "summary", "finalizer_type": "Transform", "system_prompt": "Summarize."}""",
                """{"name": "summary", "finalizer_type": "Transform", "system_prompt": "Summarize.", "settings": {"Provider": "openrouter", "Model": "openai/gpt-4o", "Url": "https://example.invalid"}}""");

        await Materializer(crew).MaterializeAsync(spec, Guid.NewGuid());

        var plain = crew.Groundings.Single(g => g.Name == "plain");
        Assert.Equal(new[] { "MaxResults" }, plain.ProviderSettings.Keys.ToArray());
        Assert.DoesNotContain(crew.Groundings, g => g.Name == "fetch");
        var summary = crew.Finalizers.Single(f => f.Name == "summary");
        Assert.False(summary.Settings.ContainsKey("Url"));
        Assert.Equal(("claude-cli", "claude-sonnet-latest"), (summary.Settings[TransformSettings.KeyProvider], summary.Settings[TransformSettings.KeyModel]));
    }

    [Fact]
    public async Task TechnicalCrewsGetTheTechnicalExecutorPrimary()
    {
        var crew = new CapturingCrewService();
        var spec = Spec.Replace("german-pre-litigation-settlement-letter", "infrastructure-architecture-review")
            .Replace("\"model_domain\": \"Legal\"", "\"model_domain\": \"Technical\"");

        await Materializer(crew).MaterializeAsync(spec, Guid.NewGuid());

        Assert.Equal(ModelDomain.Technical, crew.Template!.Domain);
        Assert.Equal("claude-fable-latest", crew.Executors.Single().Model);
        Assert.Equal("gpt-astra-latest", crew.Reviewers[1].Model);
    }

    [Theory]
    [InlineData(ModelDomain.Technical, false)]
    [InlineData(ModelDomain.Legal, true)]
    public async Task DedupReusesASimilarTemplateOnlyWithTheSameModelDomain(ModelDomain existingDomain, bool reused)
    {
        var crew = new CapturingCrewService
        {
            Existing = new CrewTemplate("custom-similar", "Similar", "d", "custom-x", ["custom-r"], EvaluationStrategy.Parallel,
                null, [], [], false, Domain: existingDomain),
        };
        var materializer = new CrewMaterializer(new NoIssuesValidator(), crew, new SimilarEmbedding("custom-similar"),
            new FixedEmbeddingProvider(), new NoTransactions(), NullLogger<CrewMaterializer>.Instance);

        var result = await materializer.MaterializeAsync(Spec, Guid.NewGuid());

        Assert.Equal(reused, result.WasDuplicate);
        if (!reused)
        {
            Assert.Equal(ModelDomain.Legal, crew.Template!.Domain);
            Assert.Contains(result.Warnings, w => w.Contains("custom-similar", StringComparison.Ordinal) && w.Contains("differs", StringComparison.Ordinal));
        }
    }

    private static CrewMaterializer Materializer(CapturingCrewService crew) =>
        new(new NoIssuesValidator(), crew, new NoEmbeddings(), new FailingEmbeddingProvider(), new NoTransactions(),
            NullLogger<CrewMaterializer>.Instance);

    private sealed class SimilarEmbedding(string templateName) : ICrewTemplateEmbeddingRepository
    {
        public Task UpsertAsync(CrewTemplateEmbedding embedding, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string templateName, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<(CrewTemplateEmbedding Entry, double Similarity)>> SearchAsync(
            float[] queryEmbedding, string? domainHint, double sameDomainBoost, int topK, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<(CrewTemplateEmbedding Entry, double Similarity)>>(
                [(new CrewTemplateEmbedding { TemplateName = templateName }, 0.99)]);
    }

    private sealed class FixedEmbeddingProvider : IEmbeddingProvider
    {
        public string ProviderName => "fixed";
        public string ModelName => "fixed";
        public int Dimensions => 1;
        public Task<EmbeddingResult> CreateAsync(string text, CancellationToken ct) => Task.FromResult(new EmbeddingResult([1f], 1, null));
        public Task<IReadOnlyList<EmbeddingResult>> CreateBatchAsync(IReadOnlyList<string> texts, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<EmbeddingResult>>(texts.Select(_ => new EmbeddingResult([1f], 1, null)).ToList());
    }

    private sealed class CapturingCrewService : StubCrewServiceBase
    {
        public List<ExecutorProfile> Executors { get; } = [];
        public List<ReviewerProfile> Reviewers { get; } = [];
        public List<AdvisorProfile> Advisors { get; } = [];
        public List<GroundingProviderProfile> Groundings { get; } = [];
        public List<FinalizerProfile> Finalizers { get; } = [];
        public CrewTemplate? Template { get; private set; }
        public CrewTemplate? Existing { get; init; }

        public override Task<CrewTemplate?> GetCrewTemplateAsync(string name, CancellationToken ct = default) =>
            Task.FromResult(Existing is not null && Existing.Name == name ? Existing : null);

        public override Task<ExecutorProfile> CreateCustomExecutorProfileAsync(ExecutorProfile p, CancellationToken ct = default) { Executors.Add(p); return Task.FromResult(p); }
        public override Task<ReviewerProfile> CreateCustomReviewerProfileAsync(ReviewerProfile p, CancellationToken ct = default) { Reviewers.Add(p); return Task.FromResult(p); }
        public override Task<AdvisorProfile> CreateCustomAdvisorProfileAsync(AdvisorProfile p, CancellationToken ct = default) { Advisors.Add(p); return Task.FromResult(p); }
        public override Task<GroundingProviderProfile> CreateCustomGroundingProviderProfileAsync(GroundingProviderProfile p, CancellationToken ct = default) { Groundings.Add(p); return Task.FromResult(p); }
        public override Task<FinalizerProfile> CreateCustomFinalizerProfileAsync(FinalizerProfile p, CancellationToken ct = default) { Finalizers.Add(p); return Task.FromResult(p); }
        public override Task<CrewTemplate> CreateCustomCrewTemplateAsync(CrewTemplate t, CancellationToken ct = default) { Template = t; return Task.FromResult(t); }
    }

    private sealed class NoIssuesValidator : ICrewSpecValidator
    {
        public Task<IReadOnlyList<CrewSpecValidationIssue>> ValidateAsync(string specJson, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CrewSpecValidationIssue>>([]);
    }

    private sealed class NoEmbeddings : ICrewTemplateEmbeddingRepository
    {
        public Task UpsertAsync(CrewTemplateEmbedding embedding, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string templateName, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<(CrewTemplateEmbedding Entry, double Similarity)>> SearchAsync(
            float[] queryEmbedding, string? domainHint, double sameDomainBoost, int topK, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<(CrewTemplateEmbedding Entry, double Similarity)>>([]);
    }

    private sealed class FailingEmbeddingProvider : IEmbeddingProvider
    {
        public string ProviderName => "none";
        public string ModelName => "none";
        public int Dimensions => 0;
        public Task<EmbeddingResult> CreateAsync(string text, CancellationToken ct) => throw new InvalidOperationException("no embeddings in tests");
        public Task<IReadOnlyList<EmbeddingResult>> CreateBatchAsync(IReadOnlyList<string> texts, CancellationToken ct) => throw new InvalidOperationException("no embeddings in tests");
    }

    private sealed class NoTransactions : IAtomicTransactionFactory
    {
        public Task<IAtomicTransaction> BeginAsync(CancellationToken ct = default) => Task.FromResult<IAtomicTransaction>(new Tx());

        private sealed class Tx : IAtomicTransaction
        {
            public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
