namespace Geef.Atelier.Core.Domain;

/// <summary>Snapshot of the generated artifact text after each pipeline iteration.</summary>
public sealed class IterationEntity
{
    public required Guid Id { get; init; }
    public required Guid RunId { get; init; }
    public required int IterationNumber { get; init; }
    public required string ArtifactText { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    // Cost tracking — null for iterations before Step16 migration or when tracking is disabled.
    public int? ExecutorInputTokens { get; set; }
    public int? ExecutorOutputTokens { get; set; }
    public decimal? ExecutorCostEur { get; set; }
    public decimal? ReviewersTotalCostEur { get; set; }
    public decimal? AdvisorsTotalCostEur { get; set; }

    public ICollection<IterationActorCostEntity> ActorCosts { get; set; } = new List<IterationActorCostEntity>();
}
