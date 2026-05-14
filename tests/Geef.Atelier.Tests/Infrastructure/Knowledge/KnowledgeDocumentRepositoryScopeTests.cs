using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Infrastructure.Persistence.Crew.Knowledge;
using Geef.Atelier.Tests.Persistence;

namespace Geef.Atelier.Tests.Infrastructure.Knowledge;

/// <summary>
/// Verifies that KnowledgeDocumentRepository correctly persists and retrieves
/// Scope and RunId fields added in Step15RunAttachments.
/// </summary>
[Collection("Postgres")]
public sealed class KnowledgeDocumentRepositoryScopeTests(PostgresFixture fixture)
{
    private KnowledgeDocumentRepository Repo() => new(fixture.NewContext());

    [Fact]
    public async Task CreateAsync_WithRunLocalScope_PersistsScopeAndRunId()
    {
        var runId = await CreateRunAsync();
        var repo = Repo();
        var doc = BuildDocument(scope: KnowledgeScope.RunLocal, runId: runId);

        var created = await repo.CreateAsync(doc, CancellationToken.None);

        Assert.Equal(KnowledgeScope.RunLocal, created.Scope);
        Assert.Equal(runId, created.RunId);

        // Re-read with a fresh context to confirm persistence
        var freshRepo = Repo();
        var fromDb = await freshRepo.GetAsync(doc.Id, CancellationToken.None);
        Assert.NotNull(fromDb);
        Assert.Equal(KnowledgeScope.RunLocal, fromDb!.Scope);
        Assert.Equal(runId, fromDb.RunId);
    }

    [Fact]
    public async Task ListByRunAsync_ReturnsOnlyDocsForThatRun()
    {
        var runIdA = await CreateRunAsync();
        var runIdB = await CreateRunAsync();
        var repo = Repo();

        var docA1 = BuildDocument(scope: KnowledgeScope.RunLocal, runId: runIdA);
        var docA2 = BuildDocument(scope: KnowledgeScope.RunLocal, runId: runIdA);
        var docB  = BuildDocument(scope: KnowledgeScope.RunLocal, runId: runIdB);
        var docG  = BuildDocument(scope: KnowledgeScope.Global, runId: null);

        await repo.CreateAsync(docA1, CancellationToken.None);
        await repo.CreateAsync(docA2, CancellationToken.None);
        await repo.CreateAsync(docB, CancellationToken.None);
        await repo.CreateAsync(docG, CancellationToken.None);

        var freshRepo = Repo();
        var forRunA = await freshRepo.ListByRunAsync(runIdA, CancellationToken.None);

        Assert.Equal(2, forRunA.Count(d => d.RunId == runIdA));
        Assert.DoesNotContain(forRunA, d => d.Id == docB.Id);
        Assert.DoesNotContain(forRunA, d => d.Id == docG.Id);
    }

    [Fact]
    public async Task ListAsync_StillWorksAndReturnsAllDocs()
    {
        var runId = await CreateRunAsync();
        var repo = Repo();

        var global = BuildDocument(scope: KnowledgeScope.Global, runId: null, title: "Global Doc " + Guid.NewGuid());
        var local  = BuildDocument(scope: KnowledgeScope.RunLocal, runId: runId, title: "Local Doc " + Guid.NewGuid());

        await repo.CreateAsync(global, CancellationToken.None);
        await repo.CreateAsync(local, CancellationToken.None);

        var freshRepo = Repo();
        var all = await freshRepo.ListAsync(null, CancellationToken.None);

        Assert.Contains(all, d => d.Id == global.Id);
        Assert.Contains(all, d => d.Id == local.Id);
    }

    [Fact]
    public async Task GetAsync_ReturnsCorrectScopeForRunLocalDoc()
    {
        var runId = await CreateRunAsync();
        var repo = Repo();
        var doc = BuildDocument(scope: KnowledgeScope.RunLocal, runId: runId);

        await repo.CreateAsync(doc, CancellationToken.None);

        var freshRepo = Repo();
        var fromDb = await freshRepo.GetAsync(doc.Id, CancellationToken.None);

        Assert.NotNull(fromDb);
        Assert.Equal(KnowledgeScope.RunLocal, fromDb!.Scope);
        Assert.Equal(runId, fromDb.RunId);
    }

    /// <summary>
    /// Creates a Run row in the DB and returns its Id so KnowledgeDocuments can FK-reference it.
    /// </summary>
    private async Task<Guid> CreateRunAsync()
    {
        await using var context = fixture.NewContext();
        var run = new RunEntity
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            Status = RunStatus.Pending,
            BriefingText = "test run",
            ConfigJson = "{}",
        };
        context.Runs.Add(run);
        await context.SaveChangesAsync();
        return run.Id;
    }

    private static KnowledgeDocument BuildDocument(
        KnowledgeScope scope = KnowledgeScope.Global,
        Guid? runId = null,
        string? title = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new KnowledgeDocument(
            Id: Guid.NewGuid(),
            Title: title ?? "Test Doc",
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
