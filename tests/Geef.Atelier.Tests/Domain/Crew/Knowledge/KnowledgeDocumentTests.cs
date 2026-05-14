using Geef.Atelier.Core.Domain.Crew.Knowledge;

namespace Geef.Atelier.Tests.Domain.Crew.Knowledge;

public sealed class KnowledgeDocumentTests
{
    private static KnowledgeDocument BuildDocument(
        Guid? id = null,
        string title = "Test Title",
        string description = "Test description",
        string originalFilename = "doc.md",
        string contentType = "text/markdown",
        long fileSizeBytes = 1024,
        string rawContent = "Hello world",
        IReadOnlyList<string>? tags = null,
        string embeddingModel = "openai/text-embedding-3-small",
        int embeddingDimensions = 1536,
        int chunkCount = 2,
        decimal? indexingCostEur = 0.001m,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null) => new(
        id ?? Guid.NewGuid(),
        title,
        description,
        originalFilename,
        contentType,
        fileSizeBytes,
        rawContent,
        tags ?? ["tag-a", "tag-b"],
        embeddingModel,
        embeddingDimensions,
        chunkCount,
        indexingCostEur,
        createdAt ?? DateTimeOffset.UtcNow,
        updatedAt ?? DateTimeOffset.UtcNow);

    [Fact]
    public void FieldAccess_AllPropertiesReadable()
    {
        var id = Guid.NewGuid();
        var created = DateTimeOffset.UtcNow;
        var doc = BuildDocument(id: id, title: "My Doc", createdAt: created, updatedAt: created);

        Assert.Equal(id, doc.Id);
        Assert.Equal("My Doc", doc.Title);
        Assert.Equal("Test description", doc.Description);
        Assert.Equal("doc.md", doc.OriginalFilename);
        Assert.Equal("text/markdown", doc.ContentType);
        Assert.Equal(1024L, doc.FileSizeBytes);
        Assert.Equal("Hello world", doc.RawContent);
        Assert.Equal(["tag-a", "tag-b"], doc.Tags);
        Assert.Equal("openai/text-embedding-3-small", doc.EmbeddingModel);
        Assert.Equal(1536, doc.EmbeddingDimensions);
        Assert.Equal(2, doc.ChunkCount);
        Assert.Equal(0.001m, doc.IndexingCostEur);
        Assert.Equal(created, doc.CreatedAt);
        Assert.Equal(created, doc.UpdatedAt);
    }

    [Fact]
    public void RecordEquality_SameValues_AreEqual()
    {
        var id = Guid.NewGuid();
        var ts = DateTimeOffset.UtcNow;
        var tags = new List<string> { "a" };

        var docA = new KnowledgeDocument(id, "T", "D", "f.md", "text/plain", 100, "c", tags, "m", 512, 1, null, ts, ts);
        var docB = new KnowledgeDocument(id, "T", "D", "f.md", "text/plain", 100, "c", tags, "m", 512, 1, null, ts, ts);

        Assert.Equal(docA, docB);
    }

    [Fact]
    public void RecordEquality_DifferentId_AreNotEqual()
    {
        var ts = DateTimeOffset.UtcNow;
        var tags = new List<string>();

        var docA = new KnowledgeDocument(Guid.NewGuid(), "T", "D", "f.md", "text/plain", 0, "", tags, "m", 0, 0, null, ts, ts);
        var docB = new KnowledgeDocument(Guid.NewGuid(), "T", "D", "f.md", "text/plain", 0, "", tags, "m", 0, 0, null, ts, ts);

        Assert.NotEqual(docA, docB);
    }

    [Fact]
    public void IndexingCostEur_CanBeNull()
    {
        var doc = BuildDocument(indexingCostEur: null);
        Assert.Null(doc.IndexingCostEur);
    }

    [Fact]
    public void Tags_EmptyList_IsSupported()
    {
        var doc = BuildDocument(tags: Array.Empty<string>());
        Assert.Empty(doc.Tags);
    }
}
