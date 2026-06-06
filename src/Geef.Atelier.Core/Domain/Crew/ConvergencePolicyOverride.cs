namespace Geef.Atelier.Core.Domain.Crew;

/// <summary>
/// Per-template overrides for the global convergence policy. Any non-null field replaces the
/// matching value from <c>ConvergenceOptions</c> when the pipeline is built for a run.
/// </summary>
/// <param name="MaxIterations">Maximum number of pipeline iterations before forced finalisation.</param>
/// <param name="AbortOnCritical">When true, the pipeline aborts as soon as a critical finding is recorded.</param>
/// <param name="DetectRegression">When true, the convergence detector looks for severity regressions across iterations.</param>
/// <param name="StagnationThreshold">Number of iterations without measurable improvement before stagnation is declared.</param>
/// <param name="MaxElapsedMinutes">Wall-clock budget for the whole run in minutes. When null the budget
/// auto-scales with <see cref="MaxIterations"/> so the iteration limit — not a hidden time limit — is the
/// binding constraint. Set explicitly to cap long runs by time.</param>
public sealed record ConvergencePolicyOverride(
    int? MaxIterations,
    bool? AbortOnCritical,
    bool? DetectRegression,
    int? StagnationThreshold,
    int? MaxElapsedMinutes = null);
