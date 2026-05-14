namespace Geef.Atelier.Core.Domain.Crew.Knowledge;

/// <summary>
/// A single result from a vector-similarity search over the knowledge base.
/// </summary>
/// <param name="Chunk">The matching <see cref="KnowledgeDocumentChunk"/>.</param>
/// <param name="DocumentTitle">Title of the parent document, denormalised for display without an extra lookup.</param>
/// <param name="Similarity">Cosine similarity between the query embedding and the chunk embedding, in the range 0..1.</param>
public sealed record VectorSearchResult(
    KnowledgeDocumentChunk Chunk,
    string DocumentTitle,
    double Similarity);
