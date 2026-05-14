namespace Geef.Atelier.Infrastructure.Persistence.Crew.Knowledge;

internal sealed class KnowledgeDocumentEntity
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string OriginalFilename { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public string RawContent { get; set; } = "";
    public string[] Tags { get; set; } = [];        // Postgres text[] natively
    public string EmbeddingModel { get; set; } = "";
    public int EmbeddingDimensions { get; set; }
    public int ChunkCount { get; set; }
    public decimal? IndexingCostEur { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation
    public ICollection<KnowledgeDocumentChunkEntity> Chunks { get; set; } = [];
}
