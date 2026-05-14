using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Persistence.Crew.Knowledge;
using Geef.Atelier.Tests.Persistence;

namespace Geef.Atelier.Tests.Infrastructure.Knowledge;

[Collection("Postgres")]
public sealed class KnowledgeDocumentRepositoryTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private KnowledgeDocumentRepository Repo() => new(fixture.NewContext());

    [Fact]
    public async Task CreateAsync_PersistsAndReturns()
    {
        var repo = Repo();
        var doc = BuildDocument(title: "My Doc", tags: ["tag-a"]);

        var created = await repo.CreateAsync(doc, CancellationToken.None);

        Assert.Equal(doc.Id, created.Id);
        Assert.Equal("My Doc", created.Title);
        Assert.Equal(["tag-a"], created.Tags.ToArray());
        Assert.Equal(doc.EmbeddingModel, created.EmbeddingModel);
        Assert.Equal(doc.EmbeddingDimensions, created.EmbeddingDimensions);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenNotFound()
    {
        var repo = Repo();

        var result = await repo.GetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ListAsync_FiltersByTag()
    {
        var repo = Repo();
        var docA = BuildDocument(tags: ["shared", "only-a"]);
        var docB = BuildDocument(tags: ["shared", "only-b"]);

        await repo.CreateAsync(docA, CancellationToken.None);
        await repo.CreateAsync(docB, CancellationToken.None);

        var filteredA = await repo.ListAsync("only-a", CancellationToken.None);
        var filteredB = await repo.ListAsync("only-b", CancellationToken.None);
        var shared = await repo.ListAsync("shared", CancellationToken.None);

        Assert.Contains(filteredA, d => d.Id == docA.Id);
        Assert.DoesNotContain(filteredA, d => d.Id == docB.Id);

        Assert.Contains(filteredB, d => d.Id == docB.Id);
        Assert.DoesNotContain(filteredB, d => d.Id == docA.Id);

        Assert.Contains(shared, d => d.Id == docA.Id);
        Assert.Contains(shared, d => d.Id == docB.Id);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesFields()
    {
        var repo = Repo();
        var doc = BuildDocument(title: "Original");
        await repo.CreateAsync(doc, CancellationToken.None);

        var updated = doc with
        {
            Title = "Updated",
            Description = "New description",
            Tags = ["new-tag"],
            ChunkCount = 5,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await repo.UpdateAsync(updated, CancellationToken.None);

        // Re-read with a fresh context
        var freshRepo = Repo();
        var fromDb = await freshRepo.GetAsync(doc.Id, CancellationToken.None);

        Assert.NotNull(fromDb);
        Assert.Equal("Updated", fromDb!.Title);
        Assert.Equal("New description", fromDb.Description);
        Assert.Equal(["new-tag"], fromDb.Tags.ToArray());
        Assert.Equal(5, fromDb.ChunkCount);
    }

    [Fact]
    public async Task DeleteAsync_RemovesDocument()
    {
        var repo = Repo();
        var doc = BuildDocument();
        await repo.CreateAsync(doc, CancellationToken.None);

        await repo.DeleteAsync(doc.Id, CancellationToken.None);

        var freshRepo = Repo();
        var fromDb = await freshRepo.GetAsync(doc.Id, CancellationToken.None);
        Assert.Null(fromDb);
    }

    [Fact]
    public async Task GetAllTagsAsync_ReturnsDistinctTags()
    {
        var repo = Repo();
        var docA = BuildDocument(tags: ["alpha", "beta"]);
        var docB = BuildDocument(tags: ["beta", "gamma"]);

        await repo.CreateAsync(docA, CancellationToken.None);
        await repo.CreateAsync(docB, CancellationToken.None);

        var tags = await repo.GetAllTagsAsync(CancellationToken.None);

        Assert.Contains("alpha", tags);
        Assert.Contains("beta", tags);
        Assert.Contains("gamma", tags);
        // beta appears in both docs but should be deduplicated
        Assert.Equal(tags.Distinct().Count(), tags.Count);
    }

    private static KnowledgeDocument BuildDocument(
        string? title = null,
        IReadOnlyList<string>? tags = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new KnowledgeDocument(
            Id: Guid.NewGuid(),
            Title: title ?? "Test Document",
            Description: "A test document",
            OriginalFilename: "test.md",
            ContentType: "text/markdown",
            FileSizeBytes: 100,
            RawContent: "Hello world",
            Tags: tags ?? ["default"],
            EmbeddingModel: "openai/text-embedding-3-small",
            EmbeddingDimensions: 1536,
            ChunkCount: 0,
            IndexingCostEur: null,
            CreatedAt: now,
            UpdatedAt: now);
    }
}
