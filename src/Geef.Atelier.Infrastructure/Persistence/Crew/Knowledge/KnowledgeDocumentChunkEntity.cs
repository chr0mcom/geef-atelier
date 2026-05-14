namespace Geef.Atelier.Infrastructure.Persistence.Crew.Knowledge;

internal sealed class KnowledgeDocumentChunkEntity
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = "";

    /// <summary>
    /// Stored as vector(1536) in Postgres. EF maps via a string value-converter
    /// because Pgvector.EntityFrameworkCore 0.3.0 is not compatible with Npgsql 10.x.
    /// Repository implementations read/write this column via raw SQL using NpgsqlParameter
    /// with the Pgvector.Vector type.
    /// </summary>
    public float[] Embedding { get; set; } = [];

    public int TokenCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Navigation
    public KnowledgeDocumentEntity Document { get; set; } = null!;
}
