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
    /// Stored as vector(1536) in Postgres. EF maps via a string value-converter because
    /// Pgvector.EntityFrameworkCore 0.3.0 is incompatible with Npgsql 10.x.
    /// The repository uses raw SQL for insert and similarity search.
    /// </summary>
    public float[] Embedding { get; set; } = [];
}
