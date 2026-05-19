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
/// <param name="AdvisorProfileNames">
/// Names of advisor profiles to consult during grounding/finalisation.
/// Defaults to an empty list when the field is absent from the JSON payload.
/// </param>
/// <param name="GroundingProviderNames">
/// Names of grounding-provider profiles to run before the executor begins.
/// Defaults to an empty list when the field is absent from the JSON payload.
/// </param>
/// <param name="FinalizerProfileNames">
/// Names of finalizer profiles to run after the crew converges, in execution order.
/// Defaults to an empty list when the field is absent from the JSON payload.
/// </param>
public sealed record CrewSpec(
    string ExecutorProfileName,
    IReadOnlyList<string> ReviewerProfileNames,
    EvaluationStrategy EvaluationStrategy,
    ConvergencePolicyOverride? ConvergenceOverride,
    IReadOnlyList<string>? AdvisorProfileNames = null,
    IReadOnlyList<string>? GroundingProviderNames = null,
    IReadOnlyList<string>? FinalizerProfileNames = null)
{
    /// <summary>Resolved advisor profile names; never null after construction.</summary>
    public IReadOnlyList<string> AdvisorProfileNames { get; init; } = AdvisorProfileNames ?? Array.Empty<string>();

    /// <summary>Resolved grounding-provider profile names; never null after construction.</summary>
    public IReadOnlyList<string> GroundingProviderNames { get; init; } = GroundingProviderNames ?? Array.Empty<string>();

    /// <summary>Resolved finalizer profile names; never null after construction.</summary>
    public IReadOnlyList<string> FinalizerProfileNames { get; init; } = FinalizerProfileNames ?? Array.Empty<string>();
}
