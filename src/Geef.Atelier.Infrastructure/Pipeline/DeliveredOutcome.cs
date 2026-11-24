using Geef.Atelier.Core.Domain;
using Geef.Sdk;
using Geef.Sdk.Results;
using Geef.Sdk.Runtime;

namespace Geef.Atelier.Infrastructure.Pipeline;

/// <summary>Outcome of a finished pipeline run and the number of the iteration whose text was delivered.</summary>
/// <param name="Outcome">What the run delivered.</param>
/// <param name="Iteration">Delivered iteration, or <c>null</c> when no text was delivered.</param>
internal sealed record DeliveredOutcome(RunOutcome Outcome, int? Iteration);

/// <summary>Derives <see cref="DeliveredOutcome"/> from the SDK result of a run that returned normally.</summary>
internal static class DeliveredOutcomeExtensions
{
    /// <summary>
    /// Converged runs deliver their last iteration. Non-converged runs deliver the best-effort iteration the SDK
    /// finalised, or nothing when the best-effort text is empty.
    /// </summary>
    public static DeliveredOutcome ToDeliveredOutcome(this GeefPipelineResult<FinalizedDocument> result)
    {
        if (result.Success)
            return new DeliveredOutcome(RunOutcome.Converged, result.History.Records.LastOrDefault()?.Iteration ?? result.TotalIterations);

        if (string.IsNullOrWhiteSpace(result.Output?.Markdown))
            return new DeliveredOutcome(RunOutcome.Failed, null);

        return new DeliveredOutcome(RunOutcome.DeliveredWithOpenPoints, result.History.BestEffortIteration());
    }

    /// <summary>
    /// Mirrors the SDK's best-effort selection (<c>GeefPipelineRunner.SelectBestIteration</c>): fewest rejected
    /// reviews, most recent iteration as tie-breaker. Must follow the SDK if that rule ever changes. Profile-based
    /// reviewers report blocking findings as <see cref="ReviewDecision.ApprovedWithWarnings"/>, never as
    /// <see cref="ReviewDecision.Rejected"/>, so with them both the SDK and this method pick the most recent iteration.
    /// </summary>
    public static int? BestEffortIteration(this IterationHistory history) =>
        history.Records
            .OrderBy(r => r.EvaluationResult.Reviews.Count(rv => rv.Decision == ReviewDecision.Rejected))
            .ThenByDescending(r => r.Iteration)
            .Select(r => (int?)r.Iteration)
            .FirstOrDefault();
}
