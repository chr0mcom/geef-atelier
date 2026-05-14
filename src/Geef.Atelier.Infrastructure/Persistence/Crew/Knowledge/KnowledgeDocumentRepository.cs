using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Core.Persistence.Crew;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.Crew.Knowledge;

/// <summary>
/// EF Core-backed implementation of <see cref="IKnowledgeDocumentRepository"/>.
/// </summary>
internal sealed class KnowledgeDocumentRepository(AtelierDbContext context) : IKnowledgeDocumentRepository
{
    /// <inheritdoc/>
    public async Task<KnowledgeDocument> CreateAsync(KnowledgeDocument document, CancellationToken ct)
    {
        var entity = new KnowledgeDocumentEntity
        {
            Id = document.Id,
            Title = document.Title,
            Description = document.Description,
            OriginalFilename = document.OriginalFilename,
            ContentType = document.ContentType,
            FileSizeBytes = document.FileSizeBytes,
            RawContent = document.RawContent,
            Tags = document.Tags.ToArray(),
            EmbeddingModel = document.EmbeddingModel,
            EmbeddingDimensions = document.EmbeddingDimensions,
            ChunkCount = document.ChunkCount,
            IndexingCostEur = document.IndexingCostEur,
            CreatedAt = document.CreatedAt,
            UpdatedAt = document.UpdatedAt,
        };

        context.KnowledgeDocuments.Add(entity);
        await context.SaveChangesAsync(ct);

        return ToModel(entity);
    }

    /// <inheritdoc/>
    public async Task<KnowledgeDocument?> GetAsync(Guid id, CancellationToken ct)
    {
        var entity = await context.KnowledgeDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, ct);

        return entity is null ? null : ToModel(entity);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<KnowledgeDocument>> ListAsync(string? tagFilter, CancellationToken ct)
    {
        var query = context.KnowledgeDocuments.AsNoTracking();

        if (tagFilter is not null)
            query = query.Where(d => d.Tags.Contains(tagFilter));

        var entities = await query.ToListAsync(ct);
        return entities.Select(ToModel).ToList();
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(KnowledgeDocument document, CancellationToken ct)
    {
        var entity = await context.KnowledgeDocuments
            .FirstOrDefaultAsync(d => d.Id == document.Id, ct)
            ?? throw new InvalidOperationException($"Knowledge document '{document.Id}' not found.");

        entity.Title = document.Title;
        entity.Description = document.Description;
        entity.OriginalFilename = document.OriginalFilename;
        entity.ContentType = document.ContentType;
        entity.FileSizeBytes = document.FileSizeBytes;
        entity.RawContent = document.RawContent;
        entity.Tags = document.Tags.ToArray();
        entity.EmbeddingModel = document.EmbeddingModel;
        entity.EmbeddingDimensions = document.EmbeddingDimensions;
        entity.ChunkCount = document.ChunkCount;
        entity.IndexingCostEur = document.IndexingCostEur;
        entity.UpdatedAt = document.UpdatedAt;

        await context.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var affected = await context.KnowledgeDocuments
            .Where(d => d.Id == id)
            .ExecuteDeleteAsync(ct);

        if (affected == 0)
            throw new InvalidOperationException($"Knowledge document '{id}' not found.");
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetAllTagsAsync(CancellationToken ct)
    {
        var tags = await context.KnowledgeDocuments
            .AsNoTracking()
            .SelectMany(d => d.Tags)
            .Distinct()
            .ToListAsync(ct);

        return tags;
    }

    private static KnowledgeDocument ToModel(KnowledgeDocumentEntity e) => new(
        Id: e.Id,
        Title: e.Title,
        Description: e.Description,
        OriginalFilename: e.OriginalFilename,
        ContentType: e.ContentType,
        FileSizeBytes: e.FileSizeBytes,
        RawContent: e.RawContent,
        Tags: e.Tags,
        EmbeddingModel: e.EmbeddingModel,
        EmbeddingDimensions: e.EmbeddingDimensions,
        ChunkCount: e.ChunkCount,
        IndexingCostEur: e.IndexingCostEur,
        CreatedAt: e.CreatedAt,
        UpdatedAt: e.UpdatedAt);
}
