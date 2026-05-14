using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Application.Crew.Knowledge.Options;
using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Core.Persistence.Crew;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Knowledge;

/// <summary>
/// Implements <see cref="IKnowledgeService"/> by orchestrating document CRUD and indexing.
/// </summary>
internal sealed class KnowledgeService(
    IKnowledgeDocumentRepository documentRepo,
    IVectorSearchRepository chunkRepo,
    DocumentIndexingService indexingService,
    IEmbeddingProvider embeddingProvider,
    IOptions<KnowledgeOptions> options,
    ILogger<KnowledgeService> logger) : IKnowledgeService
{
    /// <inheritdoc/>
    public async Task<KnowledgeDocument> UploadAsync(
        string title,
        string description,
        IReadOnlyList<string> tags,
        Stream content,
        string filename,
        string contentType,
        CancellationToken ct)
    {
        var opts = options.Value;

        // Validate content type
        if (!opts.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Content type '{contentType}' is not allowed. Allowed types: {string.Join(", ", opts.AllowedContentTypes)}.");

        // Read stream and enforce size limit
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);

        if (ms.Length > opts.MaxDocumentSizeBytes)
            throw new InvalidOperationException(
                $"Document size {ms.Length} bytes exceeds the maximum allowed size of {opts.MaxDocumentSizeBytes} bytes.");

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var rawContent = await reader.ReadToEndAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var doc = new KnowledgeDocument(
            Id: Guid.NewGuid(),
            Title: title,
            Description: description,
            OriginalFilename: filename,
            ContentType: contentType,
            FileSizeBytes: ms.Length,
            RawContent: rawContent,
            Tags: tags,
            EmbeddingModel: embeddingProvider.ModelName,
            EmbeddingDimensions: embeddingProvider.Dimensions,
            ChunkCount: 0,
            IndexingCostEur: null,
            CreatedAt: now,
            UpdatedAt: now);

        var created = await documentRepo.CreateAsync(doc, ct);

        logger.LogInformation("Created knowledge document {DocumentId} ({Filename})", created.Id, filename);

        var (chunkCount, totalCost) = await indexingService.IndexAsync(created.Id, rawContent, ct);

        var updated = created with
        {
            ChunkCount = chunkCount,
            IndexingCostEur = totalCost,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await documentRepo.UpdateAsync(updated, ct);

        return updated;
    }

    /// <inheritdoc/>
    public Task<KnowledgeDocument?> GetAsync(Guid documentId, CancellationToken ct)
        => documentRepo.GetAsync(documentId, ct);

    /// <inheritdoc/>
    public Task<IReadOnlyList<KnowledgeDocument>> ListAsync(string? tagFilter, CancellationToken ct)
        => documentRepo.ListAsync(tagFilter, ct);

    /// <inheritdoc/>
    public async Task UpdateMetadataAsync(
        Guid documentId,
        string title,
        string description,
        IReadOnlyList<string> tags,
        CancellationToken ct)
    {
        var doc = await documentRepo.GetAsync(documentId, ct)
            ?? throw new InvalidOperationException($"Knowledge document '{documentId}' not found.");

        var updated = doc with
        {
            Title = title,
            Description = description,
            Tags = tags,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await documentRepo.UpdateAsync(updated, ct);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(Guid documentId, CancellationToken ct)
    {
        await documentRepo.DeleteAsync(documentId, ct);
        logger.LogInformation("Deleted knowledge document {DocumentId}", documentId);
    }

    /// <inheritdoc/>
    public async Task ReindexAsync(Guid documentId, CancellationToken ct)
    {
        var doc = await documentRepo.GetAsync(documentId, ct)
            ?? throw new InvalidOperationException($"Knowledge document '{documentId}' not found.");

        await chunkRepo.DeleteChunksForDocumentAsync(documentId, ct);

        var (chunkCount, totalCost) = await indexingService.IndexAsync(documentId, doc.RawContent, ct);

        var updated = doc with
        {
            ChunkCount = chunkCount,
            IndexingCostEur = totalCost,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await documentRepo.UpdateAsync(updated, ct);

        logger.LogInformation("Reindexed document {DocumentId}: {ChunkCount} chunks", documentId, chunkCount);
    }

    /// <inheritdoc/>
    public async Task ReindexAllAsync(CancellationToken ct)
    {
        var documents = await documentRepo.ListAsync(null, ct);

        logger.LogInformation("Reindexing all {Count} documents", documents.Count);

        foreach (var doc in documents)
            await ReindexAsync(doc.Id, ct);
    }
}
