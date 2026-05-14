using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class KnowledgeDocumentDetailTests : TestContext
{
    private static readonly Guid TestDocId = Guid.NewGuid();

    private static KnowledgeDocument MakeDocument(Guid? id = null) =>
        new(
            Id:                  id ?? TestDocId,
            Title:               "Test Document",
            Description:         "A description.",
            OriginalFilename:    "test.md",
            ContentType:         "text/markdown",
            FileSizeBytes:       2048,
            RawContent:          "Full content.",
            Tags:                ["alpha", "beta"],
            EmbeddingModel:      "openai/text-embedding-3-small",
            EmbeddingDimensions: 1536,
            ChunkCount:          5,
            IndexingCostEur:     0.0002m,
            CreatedAt:           new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero),
            UpdatedAt:           new DateTimeOffset(2026, 5, 14, 9, 0, 0, TimeSpan.Zero),
            Scope:               KnowledgeScope.Global,
            RunId:               null);

    [Fact]
    public void DocumentFound_ShowsTitle()
    {
        var doc = MakeDocument(TestDocId);
        Services.AddSingleton<IKnowledgeService>(new StubKnowledgeService(doc));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<KnowledgeDocumentDetail>(p =>
            p.Add(c => c.DocumentId, TestDocId));

        Assert.Contains("Test Document", cut.Markup);
    }

    [Fact]
    public void DocumentFound_ShowsDetailTestId()
    {
        var doc = MakeDocument(TestDocId);
        Services.AddSingleton<IKnowledgeService>(new StubKnowledgeService(doc));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<KnowledgeDocumentDetail>(p =>
            p.Add(c => c.DocumentId, TestDocId));

        cut.Find("[data-testid='knowledge-detail']");
    }

    [Fact]
    public void DocumentNotFound_Shows404Message()
    {
        Services.AddSingleton<IKnowledgeService>(new StubKnowledgeService(null));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<KnowledgeDocumentDetail>(p =>
            p.Add(c => c.DocumentId, Guid.NewGuid()));

        cut.Find("[data-testid='not-found']");
        Assert.Contains("not found", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DocumentFound_ShowsReindexButton()
    {
        var doc = MakeDocument(TestDocId);
        Services.AddSingleton<IKnowledgeService>(new StubKnowledgeService(doc));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<KnowledgeDocumentDetail>(p =>
            p.Add(c => c.DocumentId, TestDocId));

        cut.Find("[data-testid='btn-reindex']");
    }

    [Fact]
    public void DocumentFound_ShowsDeleteButton()
    {
        var doc = MakeDocument(TestDocId);
        Services.AddSingleton<IKnowledgeService>(new StubKnowledgeService(doc));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<KnowledgeDocumentDetail>(p =>
            p.Add(c => c.DocumentId, TestDocId));

        cut.Find("[data-testid='btn-delete']");
    }

    [Fact]
    public void DocumentFound_BackLinkPointsToKnowledge()
    {
        var doc = MakeDocument(TestDocId);
        Services.AddSingleton<IKnowledgeService>(new StubKnowledgeService(doc));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<KnowledgeDocumentDetail>(p =>
            p.Add(c => c.DocumentId, TestDocId));

        var backLink = cut.Find("[data-testid='btn-back']");
        Assert.Contains("/crew/knowledge", backLink.GetAttribute("href"));
    }

    private sealed class StubKnowledgeService(KnowledgeDocument? document) : IKnowledgeService
    {
        public Task<KnowledgeDocument?> GetAsync(Guid documentId, CancellationToken ct) =>
            Task.FromResult(document);

        public Task<IReadOnlyList<KnowledgeDocument>> ListAsync(string? tagFilter, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<KnowledgeDocument>>([]);

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
