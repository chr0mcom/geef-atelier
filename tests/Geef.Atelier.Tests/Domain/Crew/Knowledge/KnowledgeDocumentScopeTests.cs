using Geef.Atelier.Core.Domain.Crew.Knowledge;

namespace Geef.Atelier.Tests.Domain.Crew.Knowledge;

public sealed class KnowledgeDocumentScopeTests
{
    private static KnowledgeDocument BuildDocument(
        KnowledgeScope scope = KnowledgeScope.Global,
        Guid? runId = null)
    {
        var ts = DateTimeOffset.UtcNow;
        return new KnowledgeDocument(
            Id: Guid.NewGuid(),
            Title: "Test",
            Description: "Desc",
            OriginalFilename: "doc.md",
            ContentType: "text/markdown",
            FileSizeBytes: 100,
            RawContent: "content",
            Tags: [],
            EmbeddingModel: "openai/text-embedding-3-small",
            EmbeddingDimensions: 1536,
            ChunkCount: 1,
            IndexingCostEur: null,
            CreatedAt: ts,
            UpdatedAt: ts,
            Scope: scope,
            RunId: runId);
    }

    [Fact]
    public void GlobalDocument_HasScopeGlobal_AndNullRunId()
    {
        var doc = BuildDocument(KnowledgeScope.Global, null);

        Assert.Equal(KnowledgeScope.Global, doc.Scope);
        Assert.Null(doc.RunId);
    }

    [Fact]
    public void RunLocalDocument_HasScopeRunLocal_AndRunIdSet()
    {
        var runId = Guid.NewGuid();
        var doc = BuildDocument(KnowledgeScope.RunLocal, runId);

        Assert.Equal(KnowledgeScope.RunLocal, doc.Scope);
        Assert.Equal(runId, doc.RunId);
    }

    [Fact]
    public void RecordEquality_TwoIdenticalGlobalDocuments_AreEqual()
    {
        var id = Guid.NewGuid();
        var ts = DateTimeOffset.UtcNow;
        var tags = new List<string>();

        var docA = new KnowledgeDocument(id, "T", "D", "f.md", "text/plain", 0, "", tags, "m", 0, 0, null, ts, ts, KnowledgeScope.Global, null);
        var docB = new KnowledgeDocument(id, "T", "D", "f.md", "text/plain", 0, "", tags, "m", 0, 0, null, ts, ts, KnowledgeScope.Global, null);

        Assert.Equal(docA, docB);
    }

    [Fact]
    public void RecordEquality_DifferentScope_AreNotEqual()
    {
        var id = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var ts = DateTimeOffset.UtcNow;
        var tags = new List<string>();

        var globalDoc = new KnowledgeDocument(id, "T", "D", "f.md", "text/plain", 0, "", tags, "m", 0, 0, null, ts, ts, KnowledgeScope.Global, null);
        var runLocalDoc = new KnowledgeDocument(id, "T", "D", "f.md", "text/plain", 0, "", tags, "m", 0, 0, null, ts, ts, KnowledgeScope.RunLocal, runId);

        Assert.NotEqual(globalDoc, runLocalDoc);
    }
}
