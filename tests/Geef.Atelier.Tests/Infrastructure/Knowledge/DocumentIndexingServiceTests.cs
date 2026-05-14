using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew.Knowledge.Chunking;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Infrastructure.Knowledge;

public sealed class DocumentIndexingServiceTests
{
    [Fact]
    public async Task IndexAsync_CreatesCorrectNumberOfChunks()
    {
        // A text that produces 3 chunks when using small chunk size
        var content = string.Join("\n\n", Enumerable.Range(1, 10).Select(i => $"Paragraph {i}: " + new string('x', 800)));

        var embeddingProvider = new FakeEmbeddingProvider(dimensions: 3);
        var chunkRepo = new SpyVectorSearchRepository();

        var service = BuildService(embeddingProvider, chunkRepo);
        var documentId = Guid.NewGuid();

        var (chunkCount, _) = await service.IndexAsync(documentId, content, CancellationToken.None);

        Assert.Equal(chunkCount, chunkRepo.Chunks.Count);
        Assert.True(chunkCount > 0);
        Assert.All(chunkRepo.Chunks, c => Assert.Equal(documentId, c.DocumentId));
    }

    [Fact]
    public async Task IndexAsync_AccumulatesTotalCost()
    {
        var content = "Hello world. This is a test document with enough text to produce at least one chunk.";

        var embeddingProvider = new FakeEmbeddingProvider(dimensions: 3, costEurPerItem: 0.001m);
        var chunkRepo = new SpyVectorSearchRepository();

        var service = BuildService(embeddingProvider, chunkRepo);

        var (chunkCount, totalCost) = await service.IndexAsync(Guid.NewGuid(), content, CancellationToken.None);

        Assert.True(chunkCount > 0);
        Assert.NotNull(totalCost);
        Assert.True(totalCost > 0);
        // Total cost should be at least cost-per-item * chunk-count
        Assert.Equal(0.001m * chunkCount, totalCost);
    }

    [Fact]
    public async Task IndexAsync_ReturnsZero_ForEmptyContent()
    {
        var embeddingProvider = new FakeEmbeddingProvider(dimensions: 3);
        var chunkRepo = new SpyVectorSearchRepository();

        var service = BuildService(embeddingProvider, chunkRepo);

        var (chunkCount, totalCost) = await service.IndexAsync(Guid.NewGuid(), string.Empty, CancellationToken.None);

        Assert.Equal(0, chunkCount);
        Assert.Null(totalCost);
        Assert.Empty(chunkRepo.Chunks);
    }

    private static DocumentIndexingService BuildService(
        IEmbeddingProvider embeddingProvider,
        IVectorSearchRepository chunkRepo)
    {
        var splitter = new RecursiveCharacterTextSplitter(maxTokens: 100, overlapTokens: 10);
        return new DocumentIndexingService(
            splitter,
            embeddingProvider,
            chunkRepo,
            NullLogger<DocumentIndexingService>.Instance);
    }

    private sealed class FakeEmbeddingProvider(int dimensions, decimal? costEurPerItem = null) : IEmbeddingProvider
    {
        public string ProviderName => "fake";
        public string ModelName => "fake/model";
        public int Dimensions => dimensions;

        public Task<EmbeddingResult> CreateAsync(string text, CancellationToken ct)
            => Task.FromResult(new EmbeddingResult(new float[dimensions], 10, costEurPerItem));

        public Task<IReadOnlyList<EmbeddingResult>> CreateBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken ct)
        {
            IReadOnlyList<EmbeddingResult> results = texts
                .Select(_ => new EmbeddingResult(new float[dimensions], 10, costEurPerItem))
                .ToList();
            return Task.FromResult(results);
        }
    }

    private sealed class SpyVectorSearchRepository : IVectorSearchRepository
    {
        public List<KnowledgeDocumentChunk> Chunks { get; } = [];

        public Task<KnowledgeDocumentChunk> CreateChunkAsync(KnowledgeDocumentChunk chunk, CancellationToken ct)
        {
            Chunks.Add(chunk);
            return Task.FromResult(chunk);
        }

        public Task DeleteChunksForDocumentAsync(Guid documentId, CancellationToken ct)
        {
            Chunks.RemoveAll(c => c.DocumentId == documentId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            float[] queryEmbedding,
            int topK,
            IReadOnlyList<string>? tagFilter,
            CancellationToken ct)
            => Task.FromResult<IReadOnlyList<VectorSearchResult>>([]);
    }
}
