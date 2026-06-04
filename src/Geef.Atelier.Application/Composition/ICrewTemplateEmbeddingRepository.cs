using Geef.Atelier.Core.Domain.Crew.Composition;

namespace Geef.Atelier.Application.Composition;

/// <summary>
/// Persistence contract for <see cref="CrewTemplateEmbedding"/> records used by the
/// crew-catalog grounding provider and the crew-composition materializer.
/// </summary>
public interface ICrewTemplateEmbeddingRepository
{
    /// <summary>
    /// Inserts or updates the embedding for the given crew template.
    /// Upsert is keyed on <see cref="CrewTemplateEmbedding.TemplateName"/>.
    /// </summary>
    Task UpsertAsync(CrewTemplateEmbedding embedding, CancellationToken ct = default);

    /// <summary>
    /// Searches for the most similar crew-template embeddings using cosine similarity,
    /// applying a <paramref name="sameDomainBoost"/> multiplier to results whose
    /// <see cref="CrewTemplateEmbedding.Domain"/> matches <paramref name="domainHint"/>.
    /// </summary>
    /// <param name="queryEmbedding">The query vector to compare against stored embeddings.</param>
    /// <param name="domainHint">When non-null, entries in this domain receive a score boost.</param>
    /// <param name="sameDomainBoost">Multiplier applied to same-domain scores (e.g. 1.5).</param>
    /// <param name="topK">Maximum number of results to return after re-ranking.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ordered list (highest similarity first) of at most <paramref name="topK"/> entries with their boosted similarity score.</returns>
    Task<IReadOnlyList<(CrewTemplateEmbedding Entry, double Similarity)>> SearchAsync(
        float[] queryEmbedding,
        string? domainHint,
        double sameDomainBoost,
        int topK,
        CancellationToken ct = default);
}
