using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Infrastructure.Persistence.Crew.Knowledge;
using Geef.Atelier.Tests.Persistence;

namespace Geef.Atelier.Tests.Infrastructure.Knowledge;

/// <summary>
/// Verifies that VectorSearchRepository.SearchAsync correctly filters by Scope and RunId.
/// </summary>
[Collection("Postgres")]
public sealed class VectorSearchRepositoryScopeTests(PostgresFixture fixture)
{
    private KnowledgeDocumentRepository DocRepo() => new(fixture.NewContext());
    private VectorSearchRepository SearchRepo() => new(fixture.NewContext());

    [Fact]
    public async Task SearchAsync_WithGlobalScopeFilter_ReturnsOnlyGlobalDocs()
    {
        var runId = await CreateRunAsync();

        var globalDoc = await DocRepo().CreateAsync(BuildDocument("Global Only " + Guid.NewGuid(), KnowledgeScope.Global, null), CancellationToken.None);
        var localDoc  = await DocRepo().CreateAsync(BuildDocument("Local Only "  + Guid.NewGuid(), KnowledgeScope.RunLocal, runId), CancellationToken.None);

        var vec = MakeUnitVector(0);
        await InsertChunkAsync(globalDoc.Id, vec);
        await InsertChunkAsync(localDoc.Id, vec);

        var results = await SearchRepo().SearchAsync(
            vec, topK: 20, tagFilter: null,
            scopeFilter: KnowledgeScope.Global, runIdFilter: null,
            CancellationToken.None);

        Assert.Contains(results, r => r.Chunk.DocumentId == globalDoc.Id);
        Assert.DoesNotContain(results, r => r.Chunk.DocumentId == localDoc.Id);
    }

    [Fact]
    public async Task SearchAsync_WithRunLocalScopeAndRunIdFilter_ReturnsOnlyDocForThatRun()
    {
        var runIdA = await CreateRunAsync();
        var runIdB = await CreateRunAsync();

        var docA = await DocRepo().CreateAsync(BuildDocument("Local A " + Guid.NewGuid(), KnowledgeScope.RunLocal, runIdA), CancellationToken.None);
        var docB = await DocRepo().CreateAsync(BuildDocument("Local B " + Guid.NewGuid(), KnowledgeScope.RunLocal, runIdB), CancellationToken.None);

        var vec = MakeUnitVector(1);
        await InsertChunkAsync(docA.Id, vec);
        await InsertChunkAsync(docB.Id, vec);

        var results = await SearchRepo().SearchAsync(
            vec, topK: 20, tagFilter: null,
            scopeFilter: KnowledgeScope.RunLocal, runIdFilter: runIdA,
            CancellationToken.None);

        Assert.Contains(results, r => r.Chunk.DocumentId == docA.Id);
        Assert.DoesNotContain(results, r => r.Chunk.DocumentId == docB.Id);
    }

    [Fact]
    public async Task SearchAsync_WithNullScopeFilter_ReturnsAllDocs()
    {
        var runId = await CreateRunAsync();

        var globalDoc = await DocRepo().CreateAsync(BuildDocument("NullScope Global " + Guid.NewGuid(), KnowledgeScope.Global, null), CancellationToken.None);
        var localDoc  = await DocRepo().CreateAsync(BuildDocument("NullScope Local "  + Guid.NewGuid(), KnowledgeScope.RunLocal, runId), CancellationToken.None);

        var vec = MakeUnitVector(2);
        await InsertChunkAsync(globalDoc.Id, vec);
        await InsertChunkAsync(localDoc.Id, vec);

        var results = await SearchRepo().SearchAsync(
            vec, topK: 20, tagFilter: null,
            scopeFilter: null, runIdFilter: null,
            CancellationToken.None);

        Assert.Contains(results, r => r.Chunk.DocumentId == globalDoc.Id);
        Assert.Contains(results, r => r.Chunk.DocumentId == localDoc.Id);
    }

    [Fact]
    public async Task SearchAsync_CosineSimilarity_StillWorksAfterScopeFilter()
    {
        var runId = await CreateRunAsync();

        // Two run-local documents: one similar to query, one not
        var similarDoc = await DocRepo().CreateAsync(
            BuildDocument("Similar " + Guid.NewGuid(), KnowledgeScope.RunLocal, runId), CancellationToken.None);
        var dissimilarDoc = await DocRepo().CreateAsync(
            BuildDocument("Dissimilar " + Guid.NewGuid(), KnowledgeScope.RunLocal, runId), CancellationToken.None);

        var queryVec    = MakeUnitVector(3);
        var similarVec  = MakeUnitVector(3);          // identical direction → highest similarity
        var oppositeVec = MakeUnitVector(3, negate: true); // opposite → lowest similarity

        await InsertChunkAsync(similarDoc.Id, similarVec);
        await InsertChunkAsync(dissimilarDoc.Id, oppositeVec);

        var results = await SearchRepo().SearchAsync(
            queryVec, topK: 10, tagFilter: null,
            scopeFilter: KnowledgeScope.RunLocal, runIdFilter: runId,
            CancellationToken.None);

        Assert.True(results.Count >= 2);

        var similarResult    = results.First(r => r.Chunk.DocumentId == similarDoc.Id);
        var dissimilarResult = results.First(r => r.Chunk.DocumentId == dissimilarDoc.Id);

        Assert.True(similarResult.Similarity > dissimilarResult.Similarity,
            $"Expected similar ({similarResult.Similarity:F3}) > dissimilar ({dissimilarResult.Similarity:F3})");
    }

    private async Task<Guid> CreateRunAsync()
    {
        await using var context = fixture.NewContext();
        var run = new RunEntity
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            Status = RunStatus.Pending,
            BriefingText = "scope test run",
            ConfigJson = "{}",
        };
        context.Runs.Add(run);
        await context.SaveChangesAsync();
        return run.Id;
    }

    private async Task InsertChunkAsync(Guid documentId, float[] embedding)
    {
        var repo = SearchRepo();
        var chunk = new KnowledgeDocumentChunk(
            Id: Guid.NewGuid(),
            DocumentId: documentId,
            ChunkIndex: 0,
            Content: "chunk content",
            Embedding: embedding,
            TokenCount: 5,
            CreatedAt: DateTimeOffset.UtcNow);
        await repo.CreateChunkAsync(chunk, CancellationToken.None);
    }

    /// <summary>Creates a 1536-dim unit vector with +1 or -1 in dimension <paramref name="dimIndex"/>.</summary>
    private static float[] MakeUnitVector(int dimIndex, bool negate = false)
    {
        var v = new float[1536];
        v[dimIndex] = negate ? -1f : 1f;
        return v;
    }

    private static KnowledgeDocument BuildDocument(string title, KnowledgeScope scope, Guid? runId)
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
            Tags: [],
            EmbeddingModel: "openai/text-embedding-3-small",
            EmbeddingDimensions: 1536,
            ChunkCount: 0,
            IndexingCostEur: null,
            CreatedAt: now,
            UpdatedAt: now,
            Scope: scope,
            RunId: runId);
    }
}
