namespace Geef.Atelier.Core.Domain.Crew;

/// <summary>
/// Inline crew specification used by the API/MCP custom-crew submission path. Bypasses the
/// template lookup: the executor and reviewer profiles are referenced by name and resolved
/// against the system + custom profile catalogues at snapshot-build time.
/// </summary>
/// <param name="ExecutorProfileName">Name of the <c>ExecutorProfile</c> to use.</param>
/// <param name="ReviewerProfileNames">Names of the <c>ReviewerProfile</c>s to use, in order.</param>
/// <param name="EvaluationStrategy">Strategy for reviewer orchestration.</param>
/// <param name="ConvergenceOverride">Optional convergence-policy overrides for this run.</param>
public sealed record CrewSpec(
    string ExecutorProfileName,
    IReadOnlyList<string> ReviewerProfileNames,
    EvaluationStrategy EvaluationStrategy,
    ConvergencePolicyOverride? ConvergenceOverride);
