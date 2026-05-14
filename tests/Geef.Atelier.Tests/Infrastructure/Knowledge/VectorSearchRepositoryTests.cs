using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Persistence.Crew.Knowledge;
using Geef.Atelier.Tests.Persistence;

namespace Geef.Atelier.Tests.Infrastructure.Knowledge;

[Collection("Postgres")]
public sealed class VectorSearchRepositoryTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private KnowledgeDocumentRepository DocRepo() => new(fixture.NewContext());
    private VectorSearchRepository ChunkRepo() => new(fixture.NewContext());

    [Fact]
    public async Task SearchAsync_ReturnsTopKResults_ByCosineDistance()
    {
        var docRepo = DocRepo();
        var chunkRepo = ChunkRepo();

        var doc = await docRepo.CreateAsync(BuildDocument("Search Doc"), CancellationToken.None);

        // Three unit vectors in the first 3 dimensions (rest zero, length 1536):
        // Chunk 0: dim[0]=1 → identical to query direction → highest similarity
        // Chunk 1: dim[1]=1 → orthogonal → similarity ~0.5
        // Chunk 2: dim[0]=-1 → opposite → lowest similarity
        var chunk0 = BuildChunk(doc.Id, 0, MakeUnitVector(0, positive: true));
        var chunk1 = BuildChunk(doc.Id, 1, MakeUnitVector(1, positive: true));
        var chunk2 = BuildChunk(doc.Id, 2, MakeUnitVector(0, positive: false));

        await chunkRepo.CreateChunkAsync(chunk0, CancellationToken.None);
        await chunkRepo.CreateChunkAsync(chunk1, CancellationToken.None);
        await chunkRepo.CreateChunkAsync(chunk2, CancellationToken.None);

        // Query with unit vector in dim[0] — chunk0 should rank first
        var results = await chunkRepo.SearchAsync(MakeUnitVector(0, positive: true), topK: 3, tagFilter: null, CancellationToken.None);

        Assert.Equal(3, results.Count);
        Assert.Equal(chunk0.Id, results[0].Chunk.Id);
        Assert.True(results[0].Similarity >= results[1].Similarity);
        Assert.True(results[1].Similarity >= results[2].Similarity);
        Assert.Equal("Search Doc", results[0].DocumentTitle);
    }

    [Fact]
    public async Task SearchAsync_FiltersBy_TagFilter()
    {
        var docRepo = DocRepo();
        var chunkRepo = ChunkRepo();

        var docA = await docRepo.CreateAsync(BuildDocument("Doc A", ["tag-alpha"]), CancellationToken.None);
        var docB = await docRepo.CreateAsync(BuildDocument("Doc B", ["tag-beta"]), CancellationToken.None);

        var chunkA = BuildChunk(docA.Id, 0, MakeUnitVector(0, positive: true));
        var chunkB = BuildChunk(docB.Id, 0, MakeUnitVector(0, positive: true));

        await chunkRepo.CreateChunkAsync(chunkA, CancellationToken.None);
        await chunkRepo.CreateChunkAsync(chunkB, CancellationToken.None);

        var results = await chunkRepo.SearchAsync(
            MakeUnitVector(0, positive: true), topK: 10, tagFilter: ["tag-alpha"], CancellationToken.None);

        Assert.All(results, r => Assert.Equal(docA.Id, r.Chunk.DocumentId));
        Assert.DoesNotContain(results, r => r.Chunk.DocumentId == docB.Id);
    }

    [Fact]
    public async Task SearchAsync_DoesNotThrow_WhenNoChunksMatchQuery()
    {
        var docRepo = DocRepo();
        var chunkRepo = ChunkRepo();

        // Use a unique tag that guarantees no results
        var results = await chunkRepo.SearchAsync(
            MakeUnitVector(0, positive: true), topK: 10, tagFilter: ["tag-that-does-not-exist-xyz"], CancellationToken.None);

        Assert.NotNull(results);
        Assert.Empty(results);
    }

    [Fact]
    public async Task DeleteChunksForDocumentAsync_RemovesOnlyTargetedChunks()
    {
        var docRepo = DocRepo();
        var chunkRepo = ChunkRepo();

        var docA = await docRepo.CreateAsync(BuildDocument("Doc To Delete From"), CancellationToken.None);
        var docB = await docRepo.CreateAsync(BuildDocument("Doc To Keep", ["keep-tag"]), CancellationToken.None);

        var chunkA1 = BuildChunk(docA.Id, 0, MakeUnitVector(2, positive: true));
        var chunkA2 = BuildChunk(docA.Id, 1, MakeUnitVector(3, positive: true));
        var chunkB = BuildChunk(docB.Id, 0, MakeUnitVector(4, positive: true));

        await chunkRepo.CreateChunkAsync(chunkA1, CancellationToken.None);
        await chunkRepo.CreateChunkAsync(chunkA2, CancellationToken.None);
        await chunkRepo.CreateChunkAsync(chunkB, CancellationToken.None);

        await chunkRepo.DeleteChunksForDocumentAsync(docA.Id, CancellationToken.None);

        // docB's chunk should still be searchable
        var results = await chunkRepo.SearchAsync(
            MakeUnitVector(4, positive: true), topK: 10, tagFilter: ["keep-tag"], CancellationToken.None);

        Assert.Contains(results, r => r.Chunk.Id == chunkB.Id);
        Assert.DoesNotContain(results, r => r.Chunk.DocumentId == docA.Id);
    }

    /// <summary>Creates a 1536-dim unit vector with +1 or -1 in dimension <paramref name="dimIndex"/>.</summary>
    private static float[] MakeUnitVector(int dimIndex, bool positive)
    {
        var v = new float[1536];
        v[dimIndex] = positive ? 1f : -1f;
        return v;
    }

    private static KnowledgeDocument BuildDocument(
        string title = "Test Doc",
        IReadOnlyList<string>? tags = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new KnowledgeDocument(
            Id: Guid.NewGuid(),
            Title: title,
            Description: "desc",
            OriginalFilename: "doc.md",
            ContentType: "text/markdown",
            FileSizeBytes: 50,
            RawContent: "content",
            Tags: tags ?? [],
            EmbeddingModel: "openai/text-embedding-3-small",
            EmbeddingDimensions: 1536,
            ChunkCount: 0,
            IndexingCostEur: null,
            CreatedAt: now,
            UpdatedAt: now);
    }

    private static KnowledgeDocumentChunk BuildChunk(Guid documentId, int index, float[] embedding) => new(
        Id: Guid.NewGuid(),
        DocumentId: documentId,
        ChunkIndex: index,
        Content: $"Chunk content {index}",
        Embedding: embedding,
        TokenCount: 10,
        CreatedAt: DateTimeOffset.UtcNow);
}
