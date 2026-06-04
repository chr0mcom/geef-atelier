namespace Geef.Atelier.Core.Domain.Crew.Composition;

/// <summary>
/// Stores a pgvector embedding for a crew template, used for similarity-based catalog retrieval
/// in the crew-catalog grounding provider and deduplication in the crew-composition materializer.
/// </summary>
public sealed class CrewTemplateEmbedding
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>References <c>CrewTemplate.Name</c>; not a FK to allow system templates.</summary>
    public string TemplateName { get; set; } = string.Empty;

    /// <summary>Domain hint used for similarity boosting (e.g. "general", "juristisch", "akademisch").</summary>
    public string Domain { get; set; } = "general";

    /// <summary>Combined text of name, displayName, and description — the text that was embedded.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>1536-dimensional embedding vector produced by the configured embedding provider.</summary>
    public float[] Embedding { get; set; } = [];

    /// <summary>UTC timestamp of the most recent upsert.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
