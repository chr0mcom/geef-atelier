using Geef.Sdk.Policies;
using Geef.Sdk.Results;
using Geef.Sdk.Runtime;

namespace Geef.Atelier.Infrastructure.Pipeline;

/// <summary>
/// Stops the review loop once two rounds in a row brought no progress (D-085, <c>Convergence:StopWithoutProgress</c>). Every
/// decision of <paramref name="inner"/> other than <see cref="ConvergenceDecision.Continue"/> (approval, round limit, time limit,
/// the SDK's own stops) is kept; only a further round is replaced by <see cref="LoopStopReasons.NoProgress"/>.
/// </summary>
internal sealed class NoProgressStopPolicy(IConvergencePolicy inner, FindingSeverity threshold) : IConvergencePolicy
{
    /// <inheritdoc/>
    public ConvergenceDecision Evaluate(IterationHistory history, EvaluationAggregate currentAggregate, TimeSpan elapsed)
    {
        var decision = inner.Evaluate(history, currentAggregate, elapsed);
        return decision == ConvergenceDecision.Continue && history.LastTwoRoundsWithoutProgress(threshold)
            ? LoopStopReasons.NoProgress
            : decision;
    }
}

/// <summary>Stop reasons of the review loop that the SDK has no value for.</summary>
internal static class LoopStopReasons
{
    /// <summary>
    /// Two rounds in a row without progress (<see cref="NoProgressStopPolicy"/>). The SDK enum has no fitting member, and
    /// <see cref="ConvergenceDecision.StopStagnant"/> would show a sentence about the same points coming back that this rule does
    /// not check. The SDK ends the loop on every value other than approval and continue, and stores the value as a number.
    /// </summary>
    public const ConvergenceDecision NoProgress = (ConvergenceDecision)101;
}
