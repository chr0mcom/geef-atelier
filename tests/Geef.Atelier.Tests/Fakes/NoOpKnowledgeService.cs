using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew.Knowledge;

namespace Geef.Atelier.Tests.Fakes;

/// <summary>
/// A no-op <see cref="IKnowledgeService"/> stub for tests that do not exercise knowledge functionality.
/// All methods throw <see cref="NotImplementedException"/> to catch accidental invocations.
/// </summary>
internal sealed class NoOpKnowledgeService : IKnowledgeService
{
    public Task<KnowledgeDocument> UploadRunAttachmentAsync(
        Guid runId, string title, Stream content, string filename,
        string contentType, CancellationToken ct)
        => throw new NotImplementedException("NoOpKnowledgeService should not be called in this test.");

    public Task<KnowledgeDocument> UploadAsync(
        string title, string description, IReadOnlyList<string> tags,
        Stream content, string filename, string contentType, CancellationToken ct)
        => throw new NotImplementedException("NoOpKnowledgeService should not be called in this test.");

    public Task<KnowledgeDocument?> GetAsync(Guid documentId, CancellationToken ct)
        => throw new NotImplementedException("NoOpKnowledgeService should not be called in this test.");

    public Task<IReadOnlyList<KnowledgeDocument>> ListAsync(string? tagFilter, CancellationToken ct, KnowledgeScope? scope = null)
        => throw new NotImplementedException("NoOpKnowledgeService should not be called in this test.");

    public Task UpdateMetadataAsync(Guid documentId, string title, string description, IReadOnlyList<string> tags, CancellationToken ct)
        => throw new NotImplementedException("NoOpKnowledgeService should not be called in this test.");

    public Task DeleteAsync(Guid documentId, CancellationToken ct)
        => throw new NotImplementedException("NoOpKnowledgeService should not be called in this test.");

    public Task ReindexAsync(Guid documentId, CancellationToken ct)
        => throw new NotImplementedException("NoOpKnowledgeService should not be called in this test.");

    public Task ReindexAllAsync(CancellationToken ct)
        => throw new NotImplementedException("NoOpKnowledgeService should not be called in this test.");

    public Task<IReadOnlyList<KnowledgeDocument>> ListRunAttachmentsAsync(Guid runId, CancellationToken ct)
        => throw new NotImplementedException("NoOpKnowledgeService should not be called in this test.");

    public Task PromoteToGlobalAsync(Guid documentId, string? newTitle, string? newDescription, IReadOnlyList<string>? additionalTags, CancellationToken ct)
        => throw new NotImplementedException("NoOpKnowledgeService should not be called in this test.");
}
