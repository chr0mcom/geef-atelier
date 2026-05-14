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
            () => service.UploadAsync("title", "desc", [], content, "doc.docx", "application/msword", CancellationToken.None));
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
            10, "content", [], "model", 1536, 0, null, now, now, KnowledgeScope.Global, null);
        await repos.DocumentRepo.CreateAsync(doc, CancellationToken.None);

        await service.DeleteAsync(doc.Id, CancellationToken.None);

        var fromRepo = await repos.DocumentRepo.GetAsync(doc.Id, CancellationToken.None);
        Assert.Null(fromRepo);
    }

    // --- UploadRunAttachmentAsync tests ---

    [Fact]
    public async Task UploadRunAttachmentAsync_SetsScopeToRunLocal()
    {
        var (service, repos) = BuildService();
        var runId = Guid.NewGuid();
        var content = new MemoryStream("attachment content"u8.ToArray());

        var result = await service.UploadRunAttachmentAsync(
            runId, "Attachment", content, "attach.md", "text/markdown", CancellationToken.None);

        Assert.Equal(KnowledgeScope.RunLocal, result.Scope);
    }

    [Fact]
    public async Task UploadRunAttachmentAsync_SetsRunIdToProvidedRunId()
    {
        var (service, repos) = BuildService();
        var runId = Guid.NewGuid();
        var content = new MemoryStream("hello"u8.ToArray());

        var result = await service.UploadRunAttachmentAsync(
            runId, "My Attachment", content, "file.txt", "text/plain", CancellationToken.None);

        Assert.Equal(runId, result.RunId);
    }

    [Fact]
    public async Task UploadRunAttachmentAsync_RejectsUnsupportedContentType()
    {
        var (service, _) = BuildService();
        var runId = Guid.NewGuid();
        var content = new MemoryStream("binary"u8.ToArray());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UploadRunAttachmentAsync(
                runId, "Word Attachment", content, "doc.docx", "application/msword", CancellationToken.None));
    }

    [Fact]
    public async Task UploadRunAttachmentAsync_SuccessPath_DocumentIsPersistedWithCorrectFields()
    {
        var (service, repos) = BuildService();
        var runId = Guid.NewGuid();
        var content = new MemoryStream("run attachment text"u8.ToArray());

        var result = await service.UploadRunAttachmentAsync(
            runId, "Title", content, "file.md", "text/markdown", CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(KnowledgeScope.RunLocal, result.Scope);
        Assert.Equal(runId, result.RunId);

        var fromRepo = await repos.DocumentRepo.GetAsync(result.Id, CancellationToken.None);
        Assert.NotNull(fromRepo);
        Assert.Equal(KnowledgeScope.RunLocal, fromRepo!.Scope);
        Assert.Equal(runId, fromRepo.RunId);
    }

    // --- PromoteToGlobalAsync tests ---

    [Fact]
    public async Task PromoteToGlobalAsync_SetsScopeToGlobal()
    {
        var (service, repos) = BuildService();
        var runId = Guid.NewGuid();
        var doc = SeedRunLocalDocument(repos.DocumentRepo, runId);

        await service.PromoteToGlobalAsync(doc.Id, null, null, null, CancellationToken.None);

        var promoted = await repos.DocumentRepo.GetAsync(doc.Id, CancellationToken.None);
        Assert.Equal(KnowledgeScope.Global, promoted!.Scope);
    }

    [Fact]
    public async Task PromoteToGlobalAsync_SetsRunIdToNull()
    {
        var (service, repos) = BuildService();
        var runId = Guid.NewGuid();
        var doc = SeedRunLocalDocument(repos.DocumentRepo, runId);

        await service.PromoteToGlobalAsync(doc.Id, null, null, null, CancellationToken.None);

        var promoted = await repos.DocumentRepo.GetAsync(doc.Id, CancellationToken.None);
        Assert.Null(promoted!.RunId);
    }

    [Fact]
    public async Task PromoteToGlobalAsync_MergesAdditionalTagsWithoutDuplicates()
    {
        var (service, repos) = BuildService();
        var runId = Guid.NewGuid();
        // Seed with tag "existing"
        var doc = SeedRunLocalDocument(repos.DocumentRepo, runId, tags: ["existing", "shared"]);

        // Promote with tags that overlap
        await service.PromoteToGlobalAsync(doc.Id, null, null, ["shared", "new"], CancellationToken.None);

        var promoted = await repos.DocumentRepo.GetAsync(doc.Id, CancellationToken.None);
        Assert.NotNull(promoted);
        // "existing", "shared", "new" — "shared" must appear only once
        Assert.Equal(3, promoted!.Tags.Count);
        Assert.Contains("existing", promoted.Tags);
        Assert.Contains("shared", promoted.Tags);
        Assert.Contains("new", promoted.Tags);
    }

    [Fact]
    public async Task PromoteToGlobalAsync_ThrowsWhenDocumentNotFound()
    {
        var (service, _) = BuildService();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PromoteToGlobalAsync(Guid.NewGuid(), null, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task PromoteToGlobalAsync_ThrowsWhenDocumentAlreadyGlobal()
    {
        var (service, repos) = BuildService();
        var now = DateTimeOffset.UtcNow;
        var globalDoc = new KnowledgeDocument(
            Guid.NewGuid(), "Global", "desc", "f.md", "text/markdown",
            10, "content", ["tag"], "model", 1536, 0, null, now, now, KnowledgeScope.Global, null);
        await repos.DocumentRepo.CreateAsync(globalDoc, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PromoteToGlobalAsync(globalDoc.Id, null, null, null, CancellationToken.None));
    }

    // --- helpers ---

    private static KnowledgeDocument SeedRunLocalDocument(
        InMemoryKnowledgeDocumentRepository repo,
        Guid runId,
        IReadOnlyList<string>? tags = null)
    {
        var now = DateTimeOffset.UtcNow;
        var doc = new KnowledgeDocument(
            Guid.NewGuid(), "RunLocal Doc", "desc", "f.md", "text/markdown",
            10, "content", tags ?? [], "model", 1536, 0, null, now, now,
            KnowledgeScope.RunLocal, runId);
        repo.CreateAsync(doc, CancellationToken.None).GetAwaiter().GetResult();
        return doc;
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
        var pdfExtractor = new PdfTextExtractor(NullLogger<PdfTextExtractor>.Instance);
        var service = new KnowledgeService(
            docRepo,
            chunkRepo,
            indexingService,
            embeddingProvider,
            pdfExtractor,
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

        public Task<IReadOnlyList<KnowledgeDocument>> ListAsync(string? tagFilter, CancellationToken ct, KnowledgeScope? scope = null)
        {
            var values = _store.Values.AsEnumerable();
            if (tagFilter is not null)
                values = values.Where(d => d.Tags.Contains(tagFilter));
            if (scope is not null)
                values = values.Where(d => d.Scope == scope.Value);
            IReadOnlyList<KnowledgeDocument> list = [.. values];
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

        public Task<IReadOnlyList<KnowledgeDocument>> ListByRunAsync(Guid runId, CancellationToken ct)
        {
            IReadOnlyList<KnowledgeDocument> list = [.. _store.Values
                .Where(d => d.RunId == runId)
                .OrderBy(d => d.CreatedAt)];
            return Task.FromResult(list);
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
            float[] queryEmbedding, int topK, IReadOnlyList<string>? tagFilter,
            KnowledgeScope? scopeFilter, Guid? runIdFilter, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<VectorSearchResult>>([]);
    }
}
