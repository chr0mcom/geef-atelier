namespace Geef.Atelier.Infrastructure.Persistence.Crew.Learning;

internal sealed class LearningEntryEntity
{
    public Guid Id { get; set; }
    public string Text { get; set; } = "";
    public Guid? SourceRunId { get; set; }
    public Guid? LearningRunId { get; set; }
    public string Domain { get; set; } = "";
    public int Status { get; set; }
    public string StructuredFactsJson { get; set; } = "";
    public string OwnerUsername { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>
    /// Stored as vector(1536) in Postgres. Null until the embedding is written via raw SQL
    /// (<see cref="LearningRepository.SetEmbeddingAsync"/>), because EF Core's string
    /// value-converter cannot cast to the Postgres vector type directly.
    /// </summary>
    public float[]? Embedding { get; set; } = null;
}
