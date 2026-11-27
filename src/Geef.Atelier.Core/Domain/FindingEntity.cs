namespace Geef.Atelier.Core.Domain;

/// <summary>A single reviewer finding attached to a pipeline iteration.</summary>
public sealed record FindingEntity
{
    public required Guid Id { get; init; }
    public required Guid IterationId { get; init; }
    public required string ReviewerName { get; init; }
    public required FindingSeverity Severity { get; init; }
    public required string Message { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Category of the finding as the reviewer set it (<c>deterministic-check</c>, <c>fact-check</c>, …); <c>null</c> when the reviewer set none.</summary>
    public string? Category { get; init; }

    /// <summary>Machine-readable code of the finding (metadata <c>code</c>, such as <c>fact.unsupported</c> or <c>length</c>); <c>null</c> when it has none.</summary>
    public string? Code { get; init; }

    /// <summary>The passage of the text the finding is about, word for word (metadata <c>quote</c>); <c>null</c> when it has none.</summary>
    public string? Quote { get; init; }
}
