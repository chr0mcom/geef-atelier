namespace Geef.Atelier.Core.Domain.Crew;

/// <summary>
/// Reusable composition of an executor profile, a list of reviewer profiles, an evaluation
/// strategy, and optional convergence overrides. Templates are looked up by <see cref="Name"/>
/// at run-submission time and materialised into a <see cref="CrewSnapshot"/>.
/// </summary>
/// <param name="Name">
/// Unique identifier. System templates (e.g. <c>"klassik"</c>) live as code constants;
/// user-created templates are auto-prefixed with <c>"custom-"</c>.
/// </param>
/// <param name="DisplayName">Human-readable name surfaced in the UI.</param>
/// <param name="Description">One- or two-sentence summary of the template's intended use.</param>
/// <param name="ExecutorProfileName">Name of the <c>ExecutorProfile</c> referenced by this template.</param>
/// <param name="ReviewerProfileNames">
/// Names of the <c>ReviewerProfile</c>s referenced by this template. Order is significant for
/// <see cref="EvaluationStrategy.Sequential"/>, <see cref="EvaluationStrategy.FailFast"/>,
/// and <see cref="EvaluationStrategy.Priority"/>.
/// </param>
/// <param name="EvaluationStrategy">Strategy that controls how reviewers are orchestrated per iteration.</param>
/// <param name="ConvergenceOverride">Optional overrides for the global convergence policy. Null means use defaults.</param>
/// <param name="AdvisorProfileNames">
/// Names of advisor profiles to consult during grounding/finalisation. Empty in PS-5 (advisor pass
/// implementation is deferred to PS-7).
/// </param>
/// <param name="GroundingProviderNames">
/// Names of grounding-provider profiles to run before the executor begins. Empty list means no web-research
/// pre-processing. Grounding runs once per run (not per iteration).
/// </param>
/// <param name="IsSystem">
/// True for templates defined as code constants in <see cref="SystemCrew"/>; false for user-created
/// templates persisted in the database. System templates are read-only at runtime.
/// </param>
public sealed record CrewTemplate(
    string Name,
    string DisplayName,
    string Description,
    string ExecutorProfileName,
    IReadOnlyList<string> ReviewerProfileNames,
    EvaluationStrategy EvaluationStrategy,
    ConvergencePolicyOverride? ConvergenceOverride,
    IReadOnlyList<string> AdvisorProfileNames,
    IReadOnlyList<string> GroundingProviderNames,
    bool IsSystem);
