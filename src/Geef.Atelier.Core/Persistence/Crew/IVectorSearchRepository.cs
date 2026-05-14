using Geef.Atelier.Core.Domain.Crew.Knowledge;

namespace Geef.Atelier.Core.Persistence.Crew;

/// <summary>
/// Persistence access for vector-search operations and <see cref="KnowledgeDocumentChunk"/> records.
/// </summary>
public interface IVectorSearchRepository
{
    /// <summary>
    /// Returns the top <paramref name="topK"/> chunks whose embeddings are most similar to
    /// <paramref name="queryEmbedding"/>, optionally filtered to chunks whose parent document
    /// contains at least one tag from <paramref name="tagFilter"/>.
    /// </summary>
    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] queryEmbedding,
        int topK,
        IReadOnlyList<string>? tagFilter,
        CancellationToken ct);

    /// <summary>Persists a new chunk and returns it.</summary>
    Task<KnowledgeDocumentChunk> CreateChunkAsync(KnowledgeDocumentChunk chunk, CancellationToken ct);

    /// <summary>Deletes all chunks associated with the specified document.</summary>
    Task DeleteChunksForDocumentAsync(Guid documentId, CancellationToken ct);
}
