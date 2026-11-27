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

    /// <summary>
    /// Review loop only (standard and resumed runs, D-085): with <see cref="LoopSwitch.On"/> a loop that ends without approval
    /// delivers the round with the fewest failed reviewers, then the fewest blocking findings, the most recent one on a tie,
    /// instead of the round the SDK picks. Default <see cref="LoopSwitch.Off"/>.
    /// </summary>
    public LoopSwitch BestDraft { get; init; } = LoopSwitch.Off;

    /// <summary>
    /// Review loop only (D-085): with <see cref="LoopSwitch.On"/> the writer gets only the blocking findings of the last round,
    /// never those of failed reviewers or below the blocking severity. Default <see cref="LoopSwitch.Off"/>.
    /// </summary>
    public LoopSwitch WriterBlockingOnly { get; init; } = LoopSwitch.Off;

    /// <summary>
    /// Review loop only (D-085): with <see cref="LoopSwitch.On"/> the loop stops once two rounds in a row brought no progress
    /// (no round better than every round before it). Default <see cref="LoopSwitch.Off"/>.
    /// </summary>
    public LoopSwitch StopWithoutProgress { get; init; } = LoopSwitch.Off;
}

/// <summary>Operating switch of one review-loop safeguard (D-085).</summary>
public enum LoopSwitch
{
    Off = 0,
    On = 1,
}
