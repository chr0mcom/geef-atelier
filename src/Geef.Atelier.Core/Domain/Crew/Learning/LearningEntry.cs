namespace Geef.Atelier.Core.Domain.Crew.Learning;

/// <summary>A structured learning extracted from a run and gated through a learning-evaluation run.</summary>
public sealed record LearningEntry(
    Guid Id,
    string Text,
    Guid SourceRunId,
    Guid? LearningRunId,
    string Domain,
    LearningStatus Status,
    string StructuredFactsJson,
    string OwnerUsername,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ApprovedAt);
