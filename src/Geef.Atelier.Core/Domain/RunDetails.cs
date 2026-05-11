namespace Geef.Atelier.Core.Domain;

/// <summary>A pipeline iteration together with its associated reviewer findings.</summary>
public sealed record IterationWithFindings(
    IterationEntity Iteration,
    IReadOnlyList<FindingEntity> Findings);

/// <summary>A run entity together with its iterations and their findings.</summary>
public sealed record RunDetails(
    RunEntity Run,
    IReadOnlyList<IterationWithFindings> Iterations);
