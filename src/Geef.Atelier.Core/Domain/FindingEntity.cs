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
}
