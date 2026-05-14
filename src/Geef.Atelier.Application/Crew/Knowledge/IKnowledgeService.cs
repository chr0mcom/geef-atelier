using Geef.Atelier.Core.Domain.Crew.Knowledge;

namespace Geef.Atelier.Application.Crew.Knowledge;

/// <summary>
/// Application-level orchestration for knowledge-base document management and indexing.
/// </summary>
public interface IKnowledgeService
{
    /// <summary>
    /// Reads the document from <paramref name="content"/>, chunks and embeds it, then persists it
    /// to the knowledge base. Returns the created <see cref="KnowledgeDocument"/>.
    /// </summary>
    Task<KnowledgeDocument> UploadAsync(
        string title,
        string description,
        IReadOnlyList<string> tags,
        Stream content,
        string filename,
        string contentType,
        CancellationToken ct);

    /// <summary>Returns the document with the given <paramref name="documentId"/>, or <c>null</c> if not found.</summary>
    Task<KnowledgeDocument?> GetAsync(Guid documentId, CancellationToken ct);

    /// <summary>
    /// Returns documents, optionally filtered by <paramref name="tagFilter"/> and/or <paramref name="scope"/>.
    /// When <paramref name="scope"/> is <c>null</c> all scopes are returned; pass
    /// <see cref="KnowledgeScope.Global"/> to restrict the result to global documents only
    /// (the Knowledge Management UI should always use that overload so run-local attachments
    /// are not shown in the global list).
    /// </summary>
    Task<IReadOnlyList<KnowledgeDocument>> ListAsync(string? tagFilter, CancellationToken ct, KnowledgeScope? scope = null);

    /// <summary>Updates the title, description, and tags of an existing document without re-indexing its content.</summary>
    Task UpdateMetadataAsync(
        Guid documentId,
        string title,
        string description,
        IReadOnlyList<string> tags,
        CancellationToken ct);

    /// <summary>Deletes the document and all its chunks from the knowledge base.</summary>
    Task DeleteAsync(Guid documentId, CancellationToken ct);

    /// <summary>Re-chunks and re-embeds a single document, replacing all its existing chunks.</summary>
    Task ReindexAsync(Guid documentId, CancellationToken ct);

    /// <summary>Re-chunks and re-embeds every document in the knowledge base.</summary>
    Task ReindexAllAsync(CancellationToken ct);

    /// <summary>
    /// Uploads a document as a run-local attachment (Scope = RunLocal, RunId set to the run).
    /// Description defaults to empty string; no tags are applied (run-local documents are
    /// ephemeral and not tag-searchable in the global knowledge base).
    /// </summary>
    Task<KnowledgeDocument> UploadRunAttachmentAsync(
        Guid runId,
        string title,
        Stream content,
        string filename,
        string contentType,
        CancellationToken ct);

    /// <summary>Lists all run-local attachment documents for a given run.</summary>
    Task<IReadOnlyList<KnowledgeDocument>> ListRunAttachmentsAsync(Guid runId, CancellationToken ct);

    /// <summary>Promotes a run-local document to global scope (Scope = Global, RunId = null).</summary>
    Task PromoteToGlobalAsync(
        Guid documentId,
        string? newTitle,
        string? newDescription,
        IReadOnlyList<string>? additionalTags,
        CancellationToken ct);
}
