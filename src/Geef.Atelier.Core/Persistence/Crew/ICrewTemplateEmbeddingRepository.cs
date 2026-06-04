namespace Geef.Atelier.Core.Persistence.Crew;

/// <summary>
/// Persistence contract for crew-template embedding vectors used for crew-level deduplication.
/// Implementations write to and query the pgvector-backed crew_template_embeddings table.
/// </summary>
public interface ICrewTemplateEmbeddingRepository
{
    /// <summary>
    /// Searches for crew templates whose stored embedding is closest to <paramref name="queryEmbedding"/>.
    /// Results are sorted by a domain-boosted cosine similarity score descending.
    /// </summary>
    /// <param name="queryEmbedding">Embedding vector of the candidate summary text.</param>
    /// <param name="currentDomain">Domain hint used to boost same-domain results.</param>
    /// <param name="sameDomainBoost">Multiplier applied to the raw similarity when the stored domain matches <paramref name="currentDomain"/>.</param>
    /// <param name="topK">Maximum number of results to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Pairs of (template name, boosted similarity score) sorted by score descending.</returns>
    Task<IReadOnlyList<(string TemplateName, double Similarity)>> SearchAsync(
        float[] queryEmbedding,
        string? currentDomain,
        double sameDomainBoost,
        int topK,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts or updates the embedding entry for the given <paramref name="templateName"/>.
    /// </summary>
    /// <param name="templateName">Name of the crew template.</param>
    /// <param name="domain">Domain of the crew template for boost calculations.</param>
    /// <param name="summaryText">Human-readable summary text that was embedded (stored for diagnostics).</param>
    /// <param name="embedding">Embedding vector to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpsertAsync(
        string templateName,
        string domain,
        string summaryText,
        float[] embedding,
        CancellationToken cancellationToken = default);
}
