namespace Geef.Atelier.Core.Domain.Crew.Advisors;

/// <summary>
/// Determines when an advisor is consulted during the pipeline run.
/// </summary>
public enum AdvisorTrigger
{
    /// <summary>The advisor is consulted once, before the executor produces the first draft.</summary>
    BeforeFirstExecution,

    /// <summary>The advisor is consulted before every executor iteration, including the first.</summary>
    BeforeEveryExecution,

    /// <summary>The advisor is consulted when the pipeline fails to converge and a recovery pass is triggered.</summary>
    OnConvergenceFailure,
}
