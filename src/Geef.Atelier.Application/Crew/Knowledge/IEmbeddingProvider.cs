namespace Geef.Atelier.Application.Crew.Knowledge;

/// <summary>
/// Abstracts an embedding model provider (e.g. OpenRouter).
/// Implementations live in the Infrastructure layer.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>Provider identifier, e.g. <c>openrouter</c>.</summary>
    string ProviderName { get; }

    /// <summary>Fully-qualified model identifier, e.g. <c>openai/text-embedding-3-small</c>.</summary>
    string ModelName { get; }

    /// <summary>Number of dimensions in each vector produced by this provider.</summary>
    int Dimensions { get; }

    /// <summary>Computes the embedding for a single <paramref name="text"/> input.</summary>
    Task<EmbeddingResult> CreateAsync(string text, CancellationToken ct);

    /// <summary>Computes embeddings for multiple texts in a single batched call where possible.</summary>
    Task<IReadOnlyList<EmbeddingResult>> CreateBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct);
}
