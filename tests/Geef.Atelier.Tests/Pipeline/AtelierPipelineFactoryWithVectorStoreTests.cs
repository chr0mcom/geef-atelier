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
/// Integration tests that verify the full pipeline correctly invokes
/// <see cref="VectorStoreGroundingProvider"/> and surfaces citations in the grounding result.
/// </summary>
public sealed class AtelierPipelineFactoryWithVectorStoreTests
{
    private static readonly Guid DocId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly float[] FakeVector = new float[1536];

    private static readonly IReadOnlyList<VectorSearchResult> TwoResults =
    [
        new VectorSearchResult(
            Chunk: new KnowledgeDocumentChunk(Guid.NewGuid(), DocId, 0, "Chunk zero content.", FakeVector, 15, DateTimeOffset.UtcNow),
            DocumentTitle: "Test Document Alpha",
            Similarity: 0.92),
        new VectorSearchResult(
            Chunk: new KnowledgeDocumentChunk(Guid.NewGuid(), DocId, 3, "Chunk three content.", FakeVector, 10, DateTimeOffset.UtcNow),
            DocumentTitle: "Test Document Beta",
            Similarity: 0.77),
    ];

    [Fact]
    public async Task EnrichAsync_WithVectorStoreProvider_CompletesWithoutThrowing()
    {
        var (groundingStep, _) = BuildGroundingStep(TwoResults);

        var runner = AtelierPipelineFactory.BuildWithProviders(
            groundingStep,
            new StubExecutionStep(),
            [new StubReviewer("stub-reviewer", Geef.Sdk.Results.FindingSeverity.Warning, "nit")],
            new MarkdownFinalizer(),
            Options.Create(new ConvergenceOptions()));

        var ex = await Record.ExceptionAsync(
            () => runner.RunAsync("Test briefing text.", CancellationToken.None));

        Assert.Null(ex);
    }

    [Fact]
    public async Task EnrichAsync_WithVectorStoreProvider_CitationsHaveExpectedCount()
    {
        var consultationRepo = new CapturingGroundingConsultationRepository();

        // Run the provider directly to inspect the result
        var directProvider = BuildProviderDirectly(TwoResults, consultationRepo);
        var profile = BuildProfile([]);
        var result = await directProvider.EnrichAsync("Test briefing.", profile, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(2, result.Citations.Count);
    }

    [Fact]
    public async Task EnrichAsync_DocumentReference_MatchesExpectedFormat()
    {
        var consultationRepo = new CapturingGroundingConsultationRepository();
        var provider = BuildProviderDirectly(TwoResults, consultationRepo);
        var profile = BuildProfile([]);

        var result = await provider.EnrichAsync("Test briefing.", profile, Guid.NewGuid(), CancellationToken.None);

        // Format: "{guid}/chunk-{N}"
        var first = result.Citations[0];
        Assert.Matches(@"^[0-9a-f\-]{36}/chunk-\d+$", first.DocumentReference ?? "");
        Assert.Equal($"{DocId}/chunk-0", first.DocumentReference);
    }

    [Fact]
    public async Task EnrichAsync_CitationUrl_IsNull()
    {
        var provider = BuildProviderDirectly(TwoResults);
        var profile = BuildProfile([]);

        var result = await provider.EnrichAsync("Test briefing.", profile, Guid.NewGuid(), CancellationToken.None);

        Assert.All(result.Citations, c => Assert.Null(c.Url));
    }

    [Fact]
    public async Task EnrichAsync_EnrichedContext_ContainsKnowledgeBaseHeading()
    {
        var provider = BuildProviderDirectly(TwoResults);
        var profile = BuildProfile([]);

        var result = await provider.EnrichAsync("Test briefing.", profile, Guid.NewGuid(), CancellationToken.None);

        Assert.Contains("Knowledge Base", result.EnrichedContext, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pipeline_WithVectorStoreSnapshotAndRealGroundingStep_EmitsGroundingContext()
    {
        var consultationRepo = new CapturingGroundingConsultationRepository();
        var (groundingStep, embeddingStub) = BuildGroundingStep(TwoResults, consultationRepo: consultationRepo);

        var runner = AtelierPipelineFactory.BuildWithProviders(
            groundingStep,
            new StubExecutionStep(),
            [new StubReviewer("stub-reviewer", Geef.Sdk.Results.FindingSeverity.Warning, "nit")],
            new MarkdownFinalizer(),
            Options.Create(new ConvergenceOptions()));

        await runner.RunAsync("Test briefing text.", CancellationToken.None);

        // The embedding provider must have been called exactly once (one grounding step)
        Assert.Equal(1, embeddingStub.CallCount);
        // A consultation record must have been persisted
        Assert.Single(consultationRepo.All);
    }

    // --- factory helpers ---

    private static (MultiProviderGroundingStep Step, CountingEmbeddingProviderStub EmbeddingStub) BuildGroundingStep(
        IReadOnlyList<VectorSearchResult> searchResults,
        CapturingGroundingConsultationRepository? consultationRepo = null)
    {
        consultationRepo ??= new CapturingGroundingConsultationRepository();
        var embeddingStub = new CountingEmbeddingProviderStub();
        var provider = BuildProviderDirectly(searchResults, consultationRepo, embeddingStub);

        var profile = BuildProfile([]);
        var factory = new SingleProviderFactory(provider);

        var inner = new BriefingGroundingStep();
        var step = new MultiProviderGroundingStep(
            inner,
            [profile],
            factory,
            Guid.NewGuid(),
            NullLogger<MultiProviderGroundingStep>.Instance);

        return (step, embeddingStub);
    }

    private static VectorStoreGroundingProvider BuildProviderDirectly(
        IReadOnlyList<VectorSearchResult> searchResults,
        CapturingGroundingConsultationRepository? consultationRepo = null,
        CountingEmbeddingProviderStub? embeddingStub = null)
    {
        consultationRepo ??= new CapturingGroundingConsultationRepository();
        embeddingStub ??= new CountingEmbeddingProviderStub();

        var services = new ServiceCollection();
        services.AddScoped<IVectorSearchRepository>(_ => new FixedVectorSearchRepository(searchResults));
        services.AddScoped<IGroundingConsultationRepository>(_ => consultationRepo);

        return new VectorStoreGroundingProvider(
            embeddingStub,
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<VectorStoreGroundingProvider>.Instance);
    }

    private static GroundingProviderProfile BuildProfile(Dictionary<string, string> settings)
        => new(
            Name: "knowledge-base",
            DisplayName: "Knowledge Base",
            Description: "Vector-store knowledge base.",
            ProviderType: "vector-store",
            ProviderSettings: settings,
            MaxQueriesPerRun: null,
            IsSystem: true);

    // --- stubs ---

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
            return Task.FromResult(new EmbeddingResult(FakeVector, 10, null));
        }

        public Task<IReadOnlyList<EmbeddingResult>> CreateBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken ct)
            => Task.FromResult<IReadOnlyList<EmbeddingResult>>(
                texts.Select(_ => new EmbeddingResult(FakeVector, 10, null)).ToList());
    }

    private sealed class FixedVectorSearchRepository(IReadOnlyList<VectorSearchResult> results) : IVectorSearchRepository
    {
        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            float[] queryEmbedding,
            int topK,
            IReadOnlyList<string>? tagFilter,
            KnowledgeScope? scopeFilter,
            Guid? runIdFilter,
            CancellationToken ct)
            => Task.FromResult(results);

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

        public Task UpdateRefinementOutcomeAsync(Guid consultationId, RefinementOutcome outcome, CancellationToken ct)
            => Task.CompletedTask;
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
