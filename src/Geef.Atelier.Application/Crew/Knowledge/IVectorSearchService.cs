using Geef.Atelier.Core.Domain.Crew.Knowledge;

namespace Geef.Atelier.Application.Crew.Knowledge;

/// <summary>
/// Application-level semantic search over the knowledge base.
/// </summary>
public interface IVectorSearchService
{
    /// <summary>
    /// Returns the top <paramref name="topK"/> chunks most similar to <paramref name="queryEmbedding"/>,
    /// optionally restricted to documents carrying at least one tag from <paramref name="tagFilter"/>.
    /// </summary>
    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] queryEmbedding,
        int topK,
        IReadOnlyList<string>? tagFilter,
        CancellationToken ct);
}
