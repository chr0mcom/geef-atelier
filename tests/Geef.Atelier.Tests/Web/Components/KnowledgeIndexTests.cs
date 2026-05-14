using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class KnowledgeIndexTests : TestContext
{
    private static KnowledgeDocument MakeDocument(string title = "My Doc", int chunkCount = 3) =>
        new(
            Id:                  Guid.NewGuid(),
            Title:               title,
            Description:         "A test document.",
            OriginalFilename:    "doc.md",
            ContentType:         "text/markdown",
            FileSizeBytes:       1024,
            RawContent:          "Content here.",
            Tags:                ["test", "doc"],
            EmbeddingModel:      "openai/text-embedding-3-small",
            EmbeddingDimensions: 1536,
            ChunkCount:          chunkCount,
            IndexingCostEur:     0.0001m,
            CreatedAt:           new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero),
            UpdatedAt:           new DateTimeOffset(2026, 5, 14, 12, 0, 0, TimeSpan.Zero),
            Scope:               KnowledgeScope.Global,
            RunId:               null);

    [Fact]
    public void EmptyList_ShowsEmptyState()
    {
        Services.AddSingleton<IKnowledgeService>(new StubKnowledgeService([]));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<KnowledgeIndex>();

        cut.Find("[data-testid='empty-state']");
        Assert.Contains("No documents yet", cut.Markup);
    }

    [Fact]
    public void WithDocuments_ShowsDocumentList()
    {
        var docs = new[] { MakeDocument("Alpha"), MakeDocument("Beta") };
        Services.AddSingleton<IKnowledgeService>(new StubKnowledgeService(docs));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<KnowledgeIndex>();

        cut.Find("[data-testid='knowledge-doc-list']");
        Assert.Contains("Alpha", cut.Markup);
        Assert.Contains("Beta", cut.Markup);
    }

    [Fact]
    public void UploadButton_HasCorrectHref()
    {
        Services.AddSingleton<IKnowledgeService>(new StubKnowledgeService([]));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<KnowledgeIndex>();

        var btn = cut.Find("[data-testid='btn-upload']");
        Assert.Contains("/crew/knowledge/upload", btn.GetAttribute("href"));
    }

    [Fact]
    public void ReindexAllButton_Exists()
    {
        Services.AddSingleton<IKnowledgeService>(new StubKnowledgeService([]));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<KnowledgeIndex>();

        cut.Find("[data-testid='btn-reindex-all']");
    }

    [Fact]
    public void ServiceError_ShowsErrorBanner()
    {
        Services.AddSingleton<IKnowledgeService>(new StubKnowledgeService(null, throwOnList: true));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<KnowledgeIndex>();

        cut.Find("[data-testid='error-banner']");
    }

    private sealed class StubKnowledgeService(
        IReadOnlyList<KnowledgeDocument>? documents,
        bool throwOnList = false) : IKnowledgeService
    {
        public Task<IReadOnlyList<KnowledgeDocument>> ListAsync(string? tagFilter, CancellationToken ct, KnowledgeScope? scope = null)
        {
            if (throwOnList) throw new InvalidOperationException("Service error");
            return Task.FromResult(documents ?? (IReadOnlyList<KnowledgeDocument>)[]);
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
