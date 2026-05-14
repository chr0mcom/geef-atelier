using Geef.Atelier.Core.Domain.Crew.Knowledge;

namespace Geef.Atelier.Tests.Domain.Crew.Knowledge;

public sealed class KnowledgeDocumentChunkTests
{
    private static KnowledgeDocumentChunk BuildChunk(
        Guid? id = null,
        Guid? documentId = null,
        int chunkIndex = 0,
        string content = "Sample chunk text",
        float[]? embedding = null,
        int tokenCount = 4,
        DateTimeOffset? createdAt = null) => new(
        id ?? Guid.NewGuid(),
        documentId ?? Guid.NewGuid(),
        chunkIndex,
        content,
        embedding ?? [0.1f, 0.2f, 0.3f],
        tokenCount,
        createdAt ?? DateTimeOffset.UtcNow);

    [Fact]
    public void FieldAccess_AllPropertiesReadable()
    {
        var id = Guid.NewGuid();
        var docId = Guid.NewGuid();
        var ts = DateTimeOffset.UtcNow;
        var embedding = new float[] { 0.5f, -0.1f };

        var chunk = new KnowledgeDocumentChunk(id, docId, 2, "Hello", embedding, 1, ts);

        Assert.Equal(id, chunk.Id);
        Assert.Equal(docId, chunk.DocumentId);
        Assert.Equal(2, chunk.ChunkIndex);
        Assert.Equal("Hello", chunk.Content);
        Assert.Equal(embedding, chunk.Embedding);
        Assert.Equal(1, chunk.TokenCount);
        Assert.Equal(ts, chunk.CreatedAt);
    }

    [Fact]
    public void RecordEquality_SameArrayInstance_AreEqual()
    {
        var now = DateTimeOffset.UtcNow;
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };
        var chunkA = new KnowledgeDocumentChunk(Guid.NewGuid(), Guid.NewGuid(), 0, "content", embedding, 5, now);
        var chunkB = chunkA with { };  // same values, same array reference
        Assert.Equal(chunkA, chunkB);
    }

    [Fact]
    public void RecordEquality_DifferentArrayInstances_AreNotEqual()
    {
        // float[] uses reference equality in C# records — two separate arrays with same contents are NOT equal
        var id = Guid.NewGuid();
        var docId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var chunkA = new KnowledgeDocumentChunk(id, docId, 0, "content", new float[] { 0.1f, 0.2f }, 5, now);
        var chunkB = new KnowledgeDocumentChunk(id, docId, 0, "content", new float[] { 0.1f, 0.2f }, 5, now);
        Assert.NotEqual(chunkA, chunkB); // different array instances, even with same values
    }

    [Fact]
    public void RecordEquality_DifferentChunkIndex_AreNotEqual()
    {
        var id = Guid.NewGuid();
        var docId = Guid.NewGuid();
        var ts = DateTimeOffset.UtcNow;
        var embedding = new float[] { 1f };

        var chunkA = new KnowledgeDocumentChunk(id, docId, 0, "text", embedding, 1, ts);
        var chunkB = new KnowledgeDocumentChunk(id, docId, 1, "text", embedding, 1, ts);

        Assert.NotEqual(chunkA, chunkB);
    }

    [Fact]
    public void Embedding_EmptyArray_IsSupported()
    {
        var chunk = BuildChunk(embedding: Array.Empty<float>());
        Assert.Empty(chunk.Embedding);
    }
}
