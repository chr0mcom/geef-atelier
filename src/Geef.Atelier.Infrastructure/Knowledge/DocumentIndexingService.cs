using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew.Knowledge.Chunking;
using Geef.Atelier.Core.Persistence.Crew;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Knowledge;

/// <summary>
/// Orchestrates the chunk → embed → persist pipeline for a single document.
/// </summary>
internal sealed class DocumentIndexingService(
    RecursiveCharacterTextSplitter splitter,
    IEmbeddingProvider embeddingProvider,
    IVectorSearchRepository chunkRepo,
    ILogger<DocumentIndexingService> logger)
{
    /// <summary>
    /// Splits <paramref name="content"/> into chunks, embeds them, and persists all chunks.
    /// </summary>
    /// <returns>Number of chunks created and total cost in EUR (null if provider reported no usage).</returns>
    public async Task<(int ChunkCount, decimal? TotalCostEur)> IndexAsync(
        Guid documentId,
        string content,
        CancellationToken ct)
    {
        var chunks = splitter.Split(content);
        if (chunks.Count == 0)
            return (0, null);

        var texts = chunks.Select(c => c.Content).ToList();
        var embeddings = await embeddingProvider.CreateBatchAsync(texts, ct);

        decimal totalCost = 0;
        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = new KnowledgeDocumentChunk(
                Id: Guid.NewGuid(),
                DocumentId: documentId,
                ChunkIndex: i,
                Content: chunks[i].Content,
                Embedding: embeddings[i].Vector,
                TokenCount: chunks[i].EstimatedTokens,
                CreatedAt: DateTimeOffset.UtcNow);

            await chunkRepo.CreateChunkAsync(chunk, ct);

            if (embeddings[i].CostEur is { } cost)
                totalCost += cost;
        }

        logger.LogInformation(
            "Indexed document {DocumentId}: {ChunkCount} chunks, cost ~{Cost:F4} EUR",
            documentId, chunks.Count, totalCost);

        return (chunks.Count, totalCost > 0 ? totalCost : null);
    }
}
