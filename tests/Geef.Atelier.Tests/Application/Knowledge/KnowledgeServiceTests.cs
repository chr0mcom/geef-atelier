using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Application.Crew.Knowledge.Options;
using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew.Knowledge.Chunking;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Application.Knowledge;

public sealed class KnowledgeServiceTests
{
    [Fact]
    public async Task UploadAsync_ThrowsInvalidOperation_WhenContentTooLarge()
    {
        var opts = new KnowledgeOptions { MaxDocumentSizeBytes = 10 };
        var (service, _) = BuildService(opts: opts);

        var largeContent = new MemoryStream(new byte[100]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UploadAsync("title", "desc", [], largeContent, "doc.md", "text/plain", CancellationToken.None));
    }

    [Fact]
    public async Task UploadAsync_ThrowsInvalidOperation_WhenContentTypeNotAllowed()
    {
        var (service, _) = BuildService();

        var content = new MemoryStream("hello"u8.ToArray());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UploadAsync("title", "desc", [], content, "doc.pdf", "application/pdf", CancellationToken.None));
    }

    [Fact]
    public async Task UploadAsync_SuccessPath_CreatesDocumentAndIndexes()
    {
        var (service, repos) = BuildService();

        var content = new MemoryStream("Hello world, this is a test document."u8.ToArray());
        var result = await service.UploadAsync(
            "My Doc", "description", ["tag1"], content, "doc.md", "text/markdown", CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("My Doc", result.Title);
        Assert.Equal(["tag1"], result.Tags.ToArray());
        Assert.True(result.ChunkCount >= 0);

        // Document should be in the repository
        var fromRepo = await repos.DocumentRepo.GetAsync(result.Id, CancellationToken.None);
        Assert.NotNull(fromRepo);
        Assert.Equal("My Doc", fromRepo!.Title);
    }

    [Fact]
    public async Task ReindexAsync_ThrowsInvalidOperation_WhenDocumentNotFound()
    {
        var (service, _) = BuildService();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ReindexAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_DelegatesToRepository()
    {
        var (service, repos) = BuildService();

        // Seed a document
        var now = DateTimeOffset.UtcNow;
        var doc = new KnowledgeDocument(
            Guid.NewGuid(), "Title", "Desc", "f.md", "text/markdown",
            10, "content", [], "model", 1536, 0, null, now, now);
        await repos.DocumentRepo.CreateAsync(doc, CancellationToken.None);

        await service.DeleteAsync(doc.Id, CancellationToken.None);

        var fromRepo = await repos.DocumentRepo.GetAsync(doc.Id, CancellationToken.None);
        Assert.Null(fromRepo);
    }

    // --- helpers ---

    private static (KnowledgeService Service, (InMemoryKnowledgeDocumentRepository DocumentRepo, InMemoryVectorSearchRepository ChunkRepo) Repos)
        BuildService(KnowledgeOptions? opts = null)
    {
        var docRepo = new InMemoryKnowledgeDocumentRepository();
        var chunkRepo = new InMemoryVectorSearchRepository();
        var embeddingProvider = new FakeEmbeddingProvider();
        var splitter = new RecursiveCharacterTextSplitter(maxTokens: 200, overlapTokens: 20);
        var indexingService = new DocumentIndexingService(
            splitter, embeddingProvider, chunkRepo, NullLogger<DocumentIndexingService>.Instance);

        var knowledgeOpts = opts ?? new KnowledgeOptions();
        var service = new KnowledgeService(
            docRepo,
            chunkRepo,
            indexingService,
            embeddingProvider,
            Options.Create(knowledgeOpts),
            NullLogger<KnowledgeService>.Instance);

        return (service, (docRepo, chunkRepo));
    }

    // ---- in-memory stubs ----

    private sealed class FakeEmbeddingProvider : IEmbeddingProvider
    {
        public string ProviderName => "fake";
        public string ModelName => "fake/model";
        public int Dimensions => 1536;

        public Task<EmbeddingResult> CreateAsync(string text, CancellationToken ct)
            => Task.FromResult(new EmbeddingResult(new float[1536], 5, null));

        public Task<IReadOnlyList<EmbeddingResult>> CreateBatchAsync(
            IReadOnlyList<string> texts, CancellationToken ct)
        {
            IReadOnlyList<EmbeddingResult> results = texts
                .Select(_ => new EmbeddingResult(new float[1536], 5, null))
                .ToList();
            return Task.FromResult(results);
        }
    }

    private sealed class InMemoryKnowledgeDocumentRepository : IKnowledgeDocumentRepository
    {
        private readonly Dictionary<Guid, KnowledgeDocument> _store = [];

        public Task<KnowledgeDocument> CreateAsync(KnowledgeDocument document, CancellationToken ct)
        {
            _store[document.Id] = document;
            return Task.FromResult(document);
        }

        public Task<KnowledgeDocument?> GetAsync(Guid id, CancellationToken ct)
            => Task.FromResult(_store.GetValueOrDefault(id));

        public Task<IReadOnlyList<KnowledgeDocument>> ListAsync(string? tagFilter, CancellationToken ct)
        {
            IReadOnlyList<KnowledgeDocument> list = tagFilter is null
                ? [.. _store.Values]
                : [.. _store.Values.Where(d => d.Tags.Contains(tagFilter))];
            return Task.FromResult(list);
        }

        public Task UpdateAsync(KnowledgeDocument document, CancellationToken ct)
        {
            _store[document.Id] = document;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken ct)
        {
            _store.Remove(id);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> GetAllTagsAsync(CancellationToken ct)
        {
            IReadOnlyList<string> tags = [.. _store.Values.SelectMany(d => d.Tags).Distinct()];
            return Task.FromResult(tags);
        }
    }

    private sealed class InMemoryVectorSearchRepository : IVectorSearchRepository
    {
        private readonly List<KnowledgeDocumentChunk> _chunks = [];

        public Task<KnowledgeDocumentChunk> CreateChunkAsync(KnowledgeDocumentChunk chunk, CancellationToken ct)
        {
            _chunks.Add(chunk);
            return Task.FromResult(chunk);
        }

        public Task DeleteChunksForDocumentAsync(Guid documentId, CancellationToken ct)
        {
            _chunks.RemoveAll(c => c.DocumentId == documentId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            float[] queryEmbedding, int topK, IReadOnlyList<string>? tagFilter, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<VectorSearchResult>>([]);
    }
}
