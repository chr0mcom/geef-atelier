using Geef.Sdk.Policies;

namespace Geef.Atelier.Infrastructure.Persistence;

/// <summary>
/// Fixed plain-English sentences for the reason a run stopped before all checks passed. They are stored at the run
/// (<c>ErrorMessage</c>) and shown to the user, so they never name an enum
/// value or other technical detail; the technical reason goes to the log only.
/// </summary>
public static class RunStopMessageExtensions
{
    /// <summary>Sentence for a stop without a known reason.</summary>
    public const string UnknownStopMessage = "The work stopped before the text was finished.";

    /// <summary>Sentence for a pipeline that ended unsuccessfully without reporting a reason.</summary>
    public const string StoppedWithOpenPointsMessage = "The work stopped before all open points were resolved.";

    /// <summary>One fixed sentence for <paramref name="reason"/>; a neutral sentence for a missing value.</summary>
    public static string ToRunStopMessage(this ConvergenceDecision? reason) =>
        reason is { } known ? known.ToRunStopMessage() : UnknownStopMessage;

    /// <summary>One fixed sentence for <paramref name="reason"/>; a neutral sentence for any value that is not a stop reason.</summary>
    public static string ToRunStopMessage(this ConvergenceDecision reason) => reason switch
    {
        ConvergenceDecision.AbortCriticalBlocker => "A check found a serious problem, so the work was stopped.",
        ConvergenceDecision.StopTimeBudgetReached => "The time limit was reached before all open points were resolved.",
        ConvergenceDecision.StopMaxAttemptsReached => "The text still had open points after the maximum number of rounds.",
        ConvergenceDecision.StopStagnant => "The text did not settle after several rounds; the same points kept coming back.",
        ConvergenceDecision.StopRegression => "The text got worse again after earlier fixes, so the work was stopped.",
        ConvergenceDecision.EscalateToHuman => "The open points could not be resolved automatically and need a person to look at them.",
        ConvergenceDecision.AbortReviewerUnavailable => "A check could not be carried out, so the work was stopped.",
        _ => UnknownStopMessage
    };
}
