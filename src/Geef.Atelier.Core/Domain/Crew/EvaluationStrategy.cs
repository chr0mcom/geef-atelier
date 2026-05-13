namespace Geef.Atelier.Core.Domain.Crew;

/// <summary>
/// Strategy for orchestrating reviewer execution within a single pipeline iteration.
/// Maps to a concrete <c>IEvaluationStrategy</c> implementation in the infrastructure layer.
/// </summary>
public enum EvaluationStrategy
{
    /// <summary>All reviewers run concurrently; all findings are collected. Default.</summary>
    Parallel = 0,

    /// <summary>Reviewers run one after another in the order listed; all findings are collected.</summary>
    Sequential = 1,

    /// <summary>Reviewers run sequentially; iteration aborts at the first critical finding.</summary>
    FailFast = 2,

    /// <summary>Reviewers run sequentially in priority order (highest priority first).</summary>
    Priority = 3,
}
