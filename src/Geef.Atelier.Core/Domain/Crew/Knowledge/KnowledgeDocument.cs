namespace Geef.Atelier.Core.Domain.Crew.Knowledge;

/// <summary>
/// Represents an uploaded document that has been indexed into the knowledge base.
/// </summary>
/// <param name="Id">Unique identifier of the document.</param>
/// <param name="Title">Human-readable title supplied at upload time.</param>
/// <param name="Description">Short description of the document's purpose or content.</param>
/// <param name="OriginalFilename">Filename as provided by the uploader.</param>
/// <param name="ContentType">MIME type of the document; either <c>text/markdown</c> or <c>text/plain</c>.</param>
/// <param name="FileSizeBytes">Size of the raw content in bytes.</param>
/// <param name="RawContent">Full text content of the document.</param>
/// <param name="Tags">Searchable tags used for filtered retrieval.</param>
/// <param name="EmbeddingModel">Model identifier used to compute embeddings, e.g. <c>openai/text-embedding-3-small</c>.</param>
/// <param name="EmbeddingDimensions">Number of dimensions in the embedding vectors.</param>
/// <param name="ChunkCount">Number of chunks the document was split into during indexing.</param>
/// <param name="IndexingCostEur">Estimated cost of indexing this document in EUR, or <c>null</c> if unavailable.</param>
/// <param name="CreatedAt">UTC timestamp when the document was first uploaded.</param>
/// <param name="UpdatedAt">UTC timestamp of the most recent metadata or content update.</param>
public sealed record KnowledgeDocument(
    Guid Id,
    string Title,
    string Description,
    string OriginalFilename,
    string ContentType,
    long FileSizeBytes,
    string RawContent,
    IReadOnlyList<string> Tags,
    string EmbeddingModel,
    int EmbeddingDimensions,
    int ChunkCount,
    decimal? IndexingCostEur,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
