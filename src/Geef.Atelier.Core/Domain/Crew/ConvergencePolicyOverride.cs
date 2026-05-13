namespace Geef.Atelier.Core.Domain.Crew;

/// <summary>
/// Per-template overrides for the global convergence policy. Any non-null field replaces the
/// matching value from <c>ConvergenceOptions</c> when the pipeline is built for a run.
/// </summary>
/// <param name="MaxIterations">Maximum number of pipeline iterations before forced finalisation.</param>
/// <param name="AbortOnCritical">When true, the pipeline aborts as soon as a critical finding is recorded.</param>
/// <param name="DetectRegression">When true, the convergence detector looks for severity regressions across iterations.</param>
/// <param name="StagnationThreshold">Number of iterations without measurable improvement before stagnation is declared.</param>
public sealed record ConvergencePolicyOverride(
    int? MaxIterations,
    bool? AbortOnCritical,
    bool? DetectRegression,
    int? StagnationThreshold);
