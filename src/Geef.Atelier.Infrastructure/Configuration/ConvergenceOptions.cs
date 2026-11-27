namespace Geef.Atelier.Infrastructure.Configuration;

public sealed class ConvergenceOptions
{
    public int MaxIterations { get; init; } = 3;
    public bool AbortOnCritical { get; init; } = false;
    public bool DetectRegression { get; init; } = true;
    public int StagnationThreshold { get; init; } = 3;

    /// <summary>
    /// Explicit global wall-clock budget in minutes. Null (the default) means the budget auto-scales
    /// with the effective MaxIterations at <see cref="MinutesPerIterationBudget"/> minutes each, so the
    /// iteration count — not a hidden time limit — is the binding constraint. This replaces the SDK's
    /// hidden 30-minute default that silently stopped long runs with a misleading StopMaxAttemptsReached.
    /// Set explicitly (here or per template) to impose a fixed time cap instead.
    /// </summary>
    public int? MaxElapsedMinutes { get; init; } = null;

    /// <summary>Per-iteration minute budget used to auto-scale the wall clock when
    /// <see cref="MaxElapsedMinutes"/> is null (default 15 min/iteration → e.g. 16 iterations = 4 h).</summary>
    public int MinutesPerIterationBudget { get; init; } = 15;
}
