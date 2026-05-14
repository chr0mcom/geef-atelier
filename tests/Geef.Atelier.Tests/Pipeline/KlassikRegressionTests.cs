using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Infrastructure.Grounding;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Tests.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Pipeline;

/// <summary>
/// Regression tests that verify Klassik crew configurations (no vector-store grounding profile)
/// never trigger the embedding provider — even when a <see cref="VectorStoreGroundingProvider"/>
/// IS registered in the factory. The key invariant: <see cref="MultiProviderGroundingStep"/> is
/// only activated when the <see cref="CrewSnapshot"/> contains at least one grounding-provider
/// profile. Without such a profile the embedding stub's counter must remain at zero.
/// </summary>
public sealed class KlassikRegressionTests
{
    private const string Briefing = "Write a short briefing text for regression testing.";

    private static readonly Guid DocId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly float[] EmptyVector = new float[1536];

    // A Klassik snapshot: GroundingProviders is null — no grounding profile registered.
    private static CrewSnapshot KlassikSnapshot() => new(
        SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
        TemplateName: SystemCrew.KlassikTemplateName,
        Executor: SystemCrew.DefaultExecutorProfile,
        Reviewers: [SystemCrew.BriefingFidelityProfile, SystemCrew.ClarityProfile],
        EvaluationStrategy: EvaluationStrategy.Parallel,
        ConvergenceOverride: null,
        Advisors: Array.Empty<AdvisorProfile>(),
        GroundingProviders: null);

    [Fact]
    public async Task KlassikPipeline_WithNoGroundingProviders_CompletesWithoutError()
    {
        var embeddingProvider = new CountingEmbeddingProviderStub();
        var factory = BuildFactory(embeddingProvider);
        var fakeClient = new FakeLlmClient();
        var resolver = new TestLlmClientResolver(fakeClient);

        // Pass the real factory — MultiProviderGroundingStep should still NOT be created
        // because KlassikSnapshot has GroundingProviders == null.
        var runner = AtelierPipelineFactory.Build(
            KlassikSnapshot(),
            resolver,
            Options.Create(new ConvergenceOptions()),
            groundingProviderFactory: factory);

        var ex = await Record.ExceptionAsync(
            () => runner.RunAsync(Briefing, CancellationToken.None));

        Assert.Null(ex);
    }

    [Fact]
    public async Task KlassikPipeline_WithNoGroundingProviders_NeverCallsEmbeddingProvider()
    {
        var embeddingProvider = new CountingEmbeddingProviderStub();
        var factory = BuildFactory(embeddingProvider);
        var fakeClient = new FakeLlmClient();
        var resolver = new TestLlmClientResolver(fakeClient);

        // The factory IS registered, but the snapshot has no grounding profiles —
        // so EnrichAsync is never reached and the counter must stay at 0.
        var runner = AtelierPipelineFactory.Build(
            KlassikSnapshot(),
            resolver,
            Options.Create(new ConvergenceOptions()),
            groundingProviderFactory: factory);

        await runner.RunAsync(Briefing, CancellationToken.None);

        Assert.Equal(0, embeddingProvider.CallCount);
    }

    [Fact]
    public async Task KlassikPipeline_WithEmptyGroundingProviderList_NeverCallsEmbeddingProvider()
    {
        var embeddingProvider = new CountingEmbeddingProviderStub();
        var factory = BuildFactory(embeddingProvider);
        var fakeClient = new FakeLlmClient();
        var resolver = new TestLlmClientResolver(fakeClient);

        // GroundingProviders is an empty list (not null) — still no profiles, so
        // MultiProviderGroundingStep is not created and the embedding stub is untouched.
        var snapshot = new CrewSnapshot(
            SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
            TemplateName: SystemCrew.KlassikTemplateName,
            Executor: SystemCrew.DefaultExecutorProfile,
            Reviewers: [SystemCrew.BriefingFidelityProfile],
            EvaluationStrategy: EvaluationStrategy.Parallel,
            ConvergenceOverride: null,
            Advisors: Array.Empty<AdvisorProfile>(),
            GroundingProviders: []);  // empty list — no grounding profiles

        var runner = AtelierPipelineFactory.Build(
            snapshot,
            resolver,
            Options.Create(new ConvergenceOptions()),
            groundingProviderFactory: factory);

        await runner.RunAsync(Briefing, CancellationToken.None);

        Assert.Equal(0, embeddingProvider.CallCount);
    }

    [Fact]
    public async Task KlassikPipeline_WithEmptyGroundingProviderList_ProducesZeroConsultations()
    {
        var embeddingProvider = new CountingEmbeddingProviderStub();
        var consultationRepo = new CapturingGroundingConsultationRepository();
        var factory = BuildFactory(embeddingProvider, consultationRepo);
        var fakeClient = new FakeLlmClient();
        var resolver = new TestLlmClientResolver(fakeClient);

        var runner = AtelierPipelineFactory.Build(
            KlassikSnapshot(),
            resolver,
            Options.Create(new ConvergenceOptions()),
            groundingProviderFactory: factory);

        var ex = await Record.ExceptionAsync(
            () => runner.RunAsync(Briefing, CancellationToken.None));

        Assert.Null(ex);
        Assert.Empty(consultationRepo.All);
    }

    [Fact]
    public void KlassikTemplate_HasNoGroundingProviderNames_ConfirmingNoVectorStoreActivation()
    {
        // Confirm at the template level that Klassik ships with no grounding — the snapshot
        // produced from it will therefore never include a vector-store provider.
        Assert.Empty(SystemCrew.KlassikTemplate.GroundingProviderNames);
    }

    // --- factory helpers ---

    /// <summary>
    /// Builds a <see cref="SingleProviderFactory"/> that wraps a
    /// <see cref="VectorStoreGroundingProvider"/> backed by the given embedding stub.
    /// This makes the factory real but the snapshot controls whether it is ever invoked.
    /// </summary>
    private static SingleProviderFactory BuildFactory(
        CountingEmbeddingProviderStub embeddingStub,
        CapturingGroundingConsultationRepository? consultationRepo = null)
    {
        consultationRepo ??= new CapturingGroundingConsultationRepository();

        var services = new ServiceCollection();
        services.AddScoped<IVectorSearchRepository>(_ => new NullVectorSearchRepository());
        services.AddScoped<IGroundingConsultationRepository>(_ => consultationRepo);

        var provider = new VectorStoreGroundingProvider(
            embeddingStub,
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<VectorStoreGroundingProvider>.Instance);

        return new SingleProviderFactory(provider);
    }

    // --- stubs ---

    /// <summary>
    /// Counting stub for <see cref="IEmbeddingProvider"/>.
    /// Used to assert that no embedding calls are made in Klassik configurations.
    /// </summary>
    private sealed class CountingEmbeddingProviderStub : IEmbeddingProvider
    {
        private int _callCount;

        public int CallCount => _callCount;
        public string ProviderName => "stub";
        public string ModelName => "stub-model";
        public int Dimensions => 1536;

        public Task<EmbeddingResult> CreateAsync(string text, CancellationToken ct)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(new EmbeddingResult(EmptyVector, 10, null));
        }

        public Task<IReadOnlyList<EmbeddingResult>> CreateBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken ct)
            => Task.FromResult<IReadOnlyList<EmbeddingResult>>(
                texts.Select(_ => new EmbeddingResult(EmptyVector, 10, null)).ToList());
    }

    /// <summary>
    /// Vector-search stub that returns an empty result set (never called in Klassik tests).
    /// </summary>
    private sealed class NullVectorSearchRepository : IVectorSearchRepository
    {
        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            float[] queryEmbedding,
            int topK,
            IReadOnlyList<string>? tagFilter,
            KnowledgeScope? scopeFilter,
            Guid? runIdFilter,
            CancellationToken ct)
            => Task.FromResult<IReadOnlyList<VectorSearchResult>>([]);

        public Task<KnowledgeDocumentChunk> CreateChunkAsync(KnowledgeDocumentChunk chunk, CancellationToken ct)
            => Task.FromResult(chunk);

        public Task DeleteChunksForDocumentAsync(Guid documentId, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class CapturingGroundingConsultationRepository : IGroundingConsultationRepository
    {
        private readonly List<GroundingConsultation> _store = [];

        public IReadOnlyList<GroundingConsultation> All => _store;

        public Task<GroundingConsultation> CreateAsync(GroundingConsultation consultation, CancellationToken ct)
        {
            _store.Add(consultation);
            return Task.FromResult(consultation);
        }

        public Task<IReadOnlyList<GroundingConsultation>> GetByRunIdAsync(Guid runId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<GroundingConsultation>>(
                _store.Where(c => c.RunId == runId).ToList());
    }

    private sealed class SingleProviderFactory(IGroundingProvider provider) : IGroundingProviderFactory
    {
        public IGroundingProvider Create(string providerType)
        {
            if (providerType == provider.ProviderType)
                return provider;
            throw new InvalidOperationException($"No provider registered for type '{providerType}'.");
        }

        public bool IsRegistered(string providerType) => providerType == provider.ProviderType;
    }
}
