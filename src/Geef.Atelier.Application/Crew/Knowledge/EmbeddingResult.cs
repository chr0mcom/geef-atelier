namespace Geef.Atelier.Application.Crew.Knowledge;

/// <summary>
/// The output of a single embedding call, including the vector and billing metadata.
/// </summary>
/// <param name="Vector">The embedding vector returned by the provider.</param>
/// <param name="TokenCount">Number of tokens consumed to produce the embedding.</param>
/// <param name="CostEur">Estimated cost in EUR, or <c>null</c> if the provider does not report usage.</param>
public sealed record EmbeddingResult(float[] Vector, int TokenCount, decimal? CostEur);
