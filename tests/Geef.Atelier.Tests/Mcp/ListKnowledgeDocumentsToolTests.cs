using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Mcp.Tools;

namespace Geef.Atelier.Tests.Mcp;

public sealed class ListKnowledgeDocumentsToolTests
{
    private static KnowledgeDocument MakeDocument(string title = "Doc A", string[] tags = null!, int chunkCount = 4) =>
        new(
            Id:                  Guid.NewGuid(),
            Title:               title,
            Description:         "Description for " + title,
            OriginalFilename:    "file.md",
            ContentType:         "text/markdown",
            FileSizeBytes:       512,
            RawContent:          "Content.",
            Tags:                tags ?? ["policy"],
            EmbeddingModel:      "openai/text-embedding-3-small",
            EmbeddingDimensions: 1536,
            ChunkCount:          chunkCount,
            IndexingCostEur:     0.0001m,
            CreatedAt:           new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt:           new DateTimeOffset(2026, 5, 14, 0, 0, 0, TimeSpan.Zero),
            Scope:               KnowledgeScope.Global,
            RunId:               null);

    [Fact]
    public async Task EmptyKnowledgeBase_ReturnsEmptyList()
    {
        var svc    = new StubKnowledgeService([]);
        var result = await ListKnowledgeDocumentsTool.ListKnowledgeDocuments(svc);

        Assert.Empty(result);
    }

    [Fact]
    public async Task WithDocuments_ReturnsDtoForEach()
    {
        var docs = new[] { MakeDocument("Alpha"), MakeDocument("Beta") };
        var svc  = new StubKnowledgeService(docs);

        var result = await ListKnowledgeDocumentsTool.ListKnowledgeDocuments(svc);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, d => d.Title == "Alpha");
        Assert.Contains(result, d => d.Title == "Beta");
    }

    [Fact]
    public async Task Dto_ContainsAllExpectedFields()
    {
        var doc = MakeDocument("My Doc", ["hr", "policy"], chunkCount: 7);
        var svc = new StubKnowledgeService([doc]);

        var result = await ListKnowledgeDocumentsTool.ListKnowledgeDocuments(svc);

        Assert.Single(result);
        var dto = result[0];
        Assert.Equal(doc.Id, dto.Id);
        Assert.Equal("My Doc", dto.Title);
        Assert.Equal(doc.Description, dto.Description);
        Assert.Equal(7, dto.ChunkCount);
        Assert.Equal(0.0001m, dto.IndexingCostEur);
        Assert.Equal(doc.CreatedAt, dto.CreatedAt);
        Assert.Equal(2, dto.Tags.Count);
        Assert.Contains("hr", dto.Tags);
        Assert.Contains("policy", dto.Tags);
    }

    [Fact]
    public async Task TagFilter_IsPassedToService()
    {
        var svc = new StubKnowledgeService([MakeDocument()]);

        await ListKnowledgeDocumentsTool.ListKnowledgeDocuments(svc, tag_filter: "hr");

        Assert.Equal("hr", svc.LastTagFilter);
    }

    [Fact]
    public async Task NoTagFilter_PassesNullToService()
    {
        var svc = new StubKnowledgeService([]);

        await ListKnowledgeDocumentsTool.ListKnowledgeDocuments(svc);

        Assert.Null(svc.LastTagFilter);
    }

    private sealed class StubKnowledgeService(IReadOnlyList<KnowledgeDocument> documents) : IKnowledgeService
    {
        public string? LastTagFilter { get; private set; } = "UNSET";

        public Task<IReadOnlyList<KnowledgeDocument>> ListAsync(string? tagFilter, CancellationToken ct)
        {
            LastTagFilter = tagFilter;
            return Task.FromResult(documents);
        }

        public Task<KnowledgeDocument?> GetAsync(Guid documentId, CancellationToken ct) =>
            Task.FromResult<KnowledgeDocument?>(null);

        public Task<KnowledgeDocument> UploadAsync(string title, string description,
            IReadOnlyList<string> tags, Stream content, string filename, string contentType,
            CancellationToken ct) => throw new NotSupportedException();

        public Task UpdateMetadataAsync(Guid documentId, string title, string description,
            IReadOnlyList<string> tags, CancellationToken ct) => Task.CompletedTask;

        public Task DeleteAsync(Guid documentId, CancellationToken ct) => Task.CompletedTask;
        public Task ReindexAsync(Guid documentId, CancellationToken ct) => Task.CompletedTask;
        public Task ReindexAllAsync(CancellationToken ct) => Task.CompletedTask;

        public Task<KnowledgeDocument> UploadRunAttachmentAsync(Guid runId, string title, Stream content, string filename, string contentType, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<KnowledgeDocument>> ListRunAttachmentsAsync(Guid runId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<KnowledgeDocument>>([]);

        public Task PromoteToGlobalAsync(Guid documentId, string? newTitle, string? newDescription, IReadOnlyList<string>? additionalTags, CancellationToken ct)
            => Task.CompletedTask;
    }
}
