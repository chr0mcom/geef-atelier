namespace Geef.Atelier.Core.Domain.Crew.Knowledge;

/// <summary>
/// A single chunk of a <see cref="KnowledgeDocument"/> together with its pre-computed embedding vector.
/// </summary>
/// <param name="Id">Unique identifier of the chunk.</param>
/// <param name="DocumentId">Identifier of the parent <see cref="KnowledgeDocument"/>.</param>
/// <param name="ChunkIndex">Zero-based position of this chunk within the document.</param>
/// <param name="Content">Text content of the chunk.</param>
/// <param name="Embedding">Pre-computed embedding vector for semantic search.</param>
/// <param name="TokenCount">Estimated number of tokens in <paramref name="Content"/>.</param>
/// <param name="CreatedAt">UTC timestamp when the chunk was created.</param>
public sealed record KnowledgeDocumentChunk(
    Guid Id,
    Guid DocumentId,
    int ChunkIndex,
    string Content,
    float[] Embedding,
    int TokenCount,
    DateTimeOffset CreatedAt);
