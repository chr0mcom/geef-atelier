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
    /// Returns all documents, optionally filtered by <paramref name="tagFilter"/>.
    /// </summary>
    Task<IReadOnlyList<KnowledgeDocument>> ListAsync(string? tagFilter, CancellationToken ct);

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
}
