namespace Geef.Atelier.Core.Domain;

/// <summary>Snapshot of the generated artifact text after each pipeline iteration.</summary>
public sealed record IterationEntity
{
    public required Guid Id { get; init; }
    public required Guid RunId { get; init; }
    public required int IterationNumber { get; init; }
    public required string ArtifactText { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
