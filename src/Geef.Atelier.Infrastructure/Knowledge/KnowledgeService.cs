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
    PdfTextExtractor pdfExtractor,
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

        if (!opts.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Content type '{contentType}' is not allowed. Allowed types: {string.Join(", ", opts.AllowedContentTypes)}.");

        var rawContent = await ReadRawContentAsync(content, contentType, opts, ct);

        var now = DateTimeOffset.UtcNow;
        var doc = new KnowledgeDocument(
            Id: Guid.NewGuid(),
            Title: title,
            Description: description,
            OriginalFilename: filename,
            ContentType: contentType,
            FileSizeBytes: 0,
            RawContent: rawContent,
            Tags: tags,
            EmbeddingModel: embeddingProvider.ModelName,
            EmbeddingDimensions: embeddingProvider.Dimensions,
            ChunkCount: 0,
            IndexingCostEur: null,
            CreatedAt: now,
            UpdatedAt: now,
            Scope: KnowledgeScope.Global,
            RunId: null);

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
    public Task<IReadOnlyList<KnowledgeDocument>> ListAsync(string? tagFilter, CancellationToken ct, KnowledgeScope? scope = null)
        => documentRepo.ListAsync(tagFilter, ct, scope);

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

    /// <inheritdoc/>
    public async Task<KnowledgeDocument> UploadRunAttachmentAsync(
        Guid runId,
        string title,
        Stream content,
        string filename,
        string contentType,
        CancellationToken ct)
    {
        var opts = options.Value;

        if (!opts.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Content type '{contentType}' is not allowed. Allowed types: {string.Join(", ", opts.AllowedContentTypes)}.");

        var rawContent = await ReadRawContentAsync(content, contentType, opts, ct);

        var now = DateTimeOffset.UtcNow;
        var doc = new KnowledgeDocument(
            Id: Guid.NewGuid(),
            Title: title,
            Description: string.Empty,
            OriginalFilename: filename,
            ContentType: contentType,
            FileSizeBytes: 0,
            RawContent: rawContent,
            Tags: [],
            EmbeddingModel: embeddingProvider.ModelName,
            EmbeddingDimensions: embeddingProvider.Dimensions,
            ChunkCount: 0,
            IndexingCostEur: null,
            CreatedAt: now,
            UpdatedAt: now,
            Scope: KnowledgeScope.RunLocal,
            RunId: runId);

        var created = await documentRepo.CreateAsync(doc, ct);

        logger.LogInformation(
            "Created run-local attachment {DocumentId} for run {RunId} ({Filename})",
            created.Id, runId, filename);

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
    public Task<IReadOnlyList<KnowledgeDocument>> ListRunAttachmentsAsync(Guid runId, CancellationToken ct)
        => documentRepo.ListByRunAsync(runId, ct);

    /// <inheritdoc/>
    public async Task PromoteToGlobalAsync(
        Guid documentId,
        string? newTitle,
        string? newDescription,
        IReadOnlyList<string>? additionalTags,
        CancellationToken ct)
    {
        var doc = await documentRepo.GetAsync(documentId, ct)
            ?? throw new InvalidOperationException($"Knowledge document '{documentId}' not found.");

        if (doc.Scope != KnowledgeScope.RunLocal)
            throw new InvalidOperationException(
                $"Document '{documentId}' is already global and cannot be promoted.");

        var promoted = doc with
        {
            Scope = KnowledgeScope.Global,
            RunId = null,
            Title = newTitle ?? doc.Title,
            Description = newDescription ?? doc.Description,
            Tags = additionalTags is { Count: > 0 }
                ? doc.Tags.Union(additionalTags).ToArray()
                : doc.Tags,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await documentRepo.UpdateAsync(promoted, ct);

        logger.LogInformation(
            "Promoted run-local document {DocumentId} to global scope", documentId);
    }

    private async Task<string> ReadRawContentAsync(
        Stream content, string contentType, KnowledgeOptions opts, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);

        var maxSize = contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
            ? opts.MaxPdfSizeBytes
            : opts.MaxDocumentSizeBytes;

        if (ms.Length > maxSize)
            throw new InvalidOperationException(
                $"File too large. Maximum size for {contentType}: {maxSize / (1024 * 1024)} MB.");

        if (contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = ms.ToArray();
            var result = pdfExtractor.ExtractText(bytes);
            if (!result.IsSuccess)
                throw new InvalidOperationException(result.ErrorMessage);
            return result.Text!;
        }

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        return await reader.ReadToEndAsync(ct);
    }
}
