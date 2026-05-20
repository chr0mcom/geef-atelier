using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Grounding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Infrastructure.Grounding;

public sealed class VectorStoreGroundingProviderTests
{
    private static readonly float[] FakeVector = [0.1f, 0.2f, 0.3f];

    [Fact]
    public async Task EnrichAsync_WithResults_ReturnsCitationsWithDocumentReference()
    {
        var docId1 = Guid.NewGuid();
        var docId2 = Guid.NewGuid();
        var searchResults = new List<VectorSearchResult>
        {
            new(
                Chunk: new KnowledgeDocumentChunk(Guid.NewGuid(), docId1, 0, "First chunk content", FakeVector, 10, DateTimeOffset.UtcNow),
                DocumentTitle: "First Document",
                Similarity: 0.95),
            new(
                Chunk: new KnowledgeDocumentChunk(Guid.NewGuid(), docId2, 2, "Second chunk content", FakeVector, 8, DateTimeOffset.UtcNow),
                DocumentTitle: "Second Document",
                Similarity: 0.80),
        };

        var (provider, _) = BuildProvider(searchResults: searchResults);
        var profile = BuildProfile([]);

        var result = await provider.EnrichAsync("test briefing", profile, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(2, result.Citations.Count);

        var first = result.Citations[0];
        Assert.Equal("First Document", first.Title);
        Assert.Null(first.Url);
        Assert.Equal("First chunk content", first.Snippet);
        Assert.Equal($"{docId1}/chunk-0", first.DocumentReference);
        Assert.Equal(0.95, first.RelevanceScore);

        var second = result.Citations[1];
        Assert.Equal("Second Document", second.Title);
        Assert.Null(second.Url);
        Assert.Equal("Second chunk content", second.Snippet);
        Assert.Equal($"{docId2}/chunk-2", second.DocumentReference);
        Assert.Equal(0.80, second.RelevanceScore);
    }

    [Fact]
    public async Task EnrichAsync_WithTagFilter_PassesTagsToRepository()
    {
        var searchRepo = new CapturingVectorSearchRepository([]);
        var (provider, _) = BuildProvider(searchRepo: searchRepo);
        var profile = BuildProfile(new Dictionary<string, string>
        {
            ["TagFilter"] = "finance,legal",
        });

        await provider.EnrichAsync("briefing text", profile, Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(searchRepo.LastTagFilter);
        Assert.Equal(2, searchRepo.LastTagFilter!.Count);
        Assert.Contains("finance", searchRepo.LastTagFilter);
        Assert.Contains("legal", searchRepo.LastTagFilter);
    }

    [Fact]
    public async Task EnrichAsync_DefaultsTopKTo5_WhenNotInProfile()
    {
        var searchRepo = new CapturingVectorSearchRepository([]);
        var (provider, _) = BuildProvider(searchRepo: searchRepo);
        var profile = BuildProfile([]);

        await provider.EnrichAsync("briefing text", profile, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(5, searchRepo.LastTopK);
    }

    [Fact]
    public async Task EnrichAsync_EmptyResults_ReturnsEmptyGroundingResult()
    {
        var (provider, _) = BuildProvider(searchResults: []);
        var profile = BuildProfile([]);

        var result = await provider.EnrichAsync("briefing text", profile, Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(result.Citations);
        Assert.Empty(result.EnrichedContext);
    }

    // --- helpers ---

    private static (VectorStoreGroundingProvider, InMemoryGroundingConsultationRepository) BuildProvider(
        IReadOnlyList<VectorSearchResult>? searchResults = null,
        CapturingVectorSearchRepository? searchRepo = null,
        InMemoryGroundingConsultationRepository? consultationRepo = null)
    {
        consultationRepo ??= new InMemoryGroundingConsultationRepository();
        var embeddingProvider = new StubEmbeddingProvider();
        var vectorRepo = searchRepo ?? new CapturingVectorSearchRepository(searchResults ?? []);

        var services = new ServiceCollection();
        services.AddScoped<IVectorSearchRepository>(_ => vectorRepo);
        services.AddScoped<IGroundingConsultationRepository>(_ => consultationRepo);

        var provider = new VectorStoreGroundingProvider(
            embeddingProvider,
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<VectorStoreGroundingProvider>.Instance);

        return (provider, consultationRepo);
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

    private sealed class StubEmbeddingProvider : IEmbeddingProvider
    {
        public string ProviderName => "stub";
        public string ModelName => "stub-model";
        public int Dimensions => 3;

        public Task<EmbeddingResult> CreateAsync(string text, CancellationToken ct)
            => Task.FromResult(new EmbeddingResult(FakeVector, 10, null));

        public Task<IReadOnlyList<EmbeddingResult>> CreateBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken ct)
            => Task.FromResult<IReadOnlyList<EmbeddingResult>>(
                texts.Select(_ => new EmbeddingResult(FakeVector, 10, null)).ToList());
    }

    private sealed class CapturingVectorSearchRepository(IReadOnlyList<VectorSearchResult> results) : IVectorSearchRepository
    {
        public int LastTopK { get; private set; }
        public IReadOnlyList<string>? LastTagFilter { get; private set; }
        public KnowledgeScope? LastScopeFilter { get; private set; }
        public Guid? LastRunIdFilter { get; private set; }

        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            float[] queryEmbedding,
            int topK,
            IReadOnlyList<string>? tagFilter,
            KnowledgeScope? scopeFilter,
            Guid? runIdFilter,
            CancellationToken ct)
        {
            LastTopK = topK;
            LastTagFilter = tagFilter;
            LastScopeFilter = scopeFilter;
            LastRunIdFilter = runIdFilter;
            return Task.FromResult(results);
        }

        public Task<KnowledgeDocumentChunk> CreateChunkAsync(KnowledgeDocumentChunk chunk, CancellationToken ct)
            => Task.FromResult(chunk);

        public Task DeleteChunksForDocumentAsync(Guid documentId, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class InMemoryGroundingConsultationRepository : IGroundingConsultationRepository
    {
        private readonly List<GroundingConsultation> _store = [];

        public Task<GroundingConsultation> CreateAsync(GroundingConsultation consultation, CancellationToken ct)
        {
            _store.Add(consultation);
            return Task.FromResult(consultation);
        }

        public Task<IReadOnlyList<GroundingConsultation>> GetByRunIdAsync(Guid runId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<GroundingConsultation>>(_store.Where(c => c.RunId == runId).ToList());

        public Task UpdateRefinementOutcomeAsync(Guid consultationId, RefinementOutcome outcome, CancellationToken ct)
            => Task.CompletedTask;
    }
}
