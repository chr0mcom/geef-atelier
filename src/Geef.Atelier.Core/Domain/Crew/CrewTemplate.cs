namespace Geef.Atelier.Core.Domain.Crew;

/// <summary>
/// A setup that exists only in code (<see cref="SystemCrew.CodeOnlyTemplates"/>): a writer profile, the check profiles, the evaluation
/// strategy and the convergence overrides. <c>CrewService</c> turns it into the <see cref="CrewSnapshot"/> of a run.
/// </summary>
/// <param name="Name">Unique name, stored as <c>Runs.CrewTemplateName</c>.</param>
/// <param name="DisplayName">Human-readable name.</param>
/// <param name="Description">One- or two-sentence summary.</param>
/// <param name="ExecutorProfileName">Name of the writer profile in <see cref="SystemCrew.CodeOnlyExecutorProfiles"/>.</param>
/// <param name="ReviewerProfileNames">Names of the check profiles in <see cref="SystemCrew.CodeOnlyReviewerProfiles"/>, in their order.</param>
/// <param name="EvaluationStrategy">Strategy that controls how the checks run per round.</param>
/// <param name="ConvergenceOverride">Overrides for the global convergence policy; null means the defaults.</param>
/// <param name="Domain">Domain the setup works in; selects the model-matrix row (null = General).</param>
public sealed record CrewTemplate(
    string Name,
    string DisplayName,
    string Description,
    string ExecutorProfileName,
    IReadOnlyList<string> ReviewerProfileNames,
    EvaluationStrategy EvaluationStrategy,
    ConvergencePolicyOverride? ConvergenceOverride,
    Models.ModelDomain? Domain = null);
