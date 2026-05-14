using Geef.Atelier.Core.Domain.Crew.Knowledge;

namespace Geef.Atelier.Core.Persistence.Crew;

/// <summary>
/// Persistence access for <see cref="KnowledgeDocument"/> records.
/// </summary>
public interface IKnowledgeDocumentRepository
{
    /// <summary>Persists a new document and returns it.</summary>
    Task<KnowledgeDocument> CreateAsync(KnowledgeDocument document, CancellationToken ct);

    /// <summary>Returns the document with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
    Task<KnowledgeDocument?> GetAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Returns documents, optionally filtered to those that contain <paramref name="tagFilter"/> in their
    /// tag list and/or match the given <paramref name="scope"/>. Pass <c>null</c> for either parameter to
    /// skip that filter.
    /// </summary>
    Task<IReadOnlyList<KnowledgeDocument>> ListAsync(string? tagFilter, CancellationToken ct, KnowledgeScope? scope = null);

    /// <summary>Replaces the stored document with the supplied record (matched by <see cref="KnowledgeDocument.Id"/>).</summary>
    Task UpdateAsync(KnowledgeDocument document, CancellationToken ct);

    /// <summary>Deletes the document with the given <paramref name="id"/> and all its associated chunks.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct);

    /// <summary>Returns the distinct union of all tags across all documents.</summary>
    Task<IReadOnlyList<string>> GetAllTagsAsync(CancellationToken ct);

    /// <summary>Returns all documents whose <c>RunId</c> matches <paramref name="runId"/>, ordered by creation time.</summary>
    Task<IReadOnlyList<KnowledgeDocument>> ListByRunAsync(Guid runId, CancellationToken ct);
}
