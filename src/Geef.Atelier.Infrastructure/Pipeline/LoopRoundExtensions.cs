using Geef.Sdk.Results;
using Geef.Sdk.Runtime;

namespace Geef.Atelier.Infrastructure.Pipeline;

/// <summary>
/// The rank of one review round (D-085): first how many reviewers failed, then how many blocking findings it has; smaller is
/// better. A round in which a reviewer failed has fewer findings because less was checked, so it never wins on that alone.
/// </summary>
/// <param name="FailedReviewers">Reviewers whose review failed for technical reasons.</param>
/// <param name="BlockingFindings">The round's count from <see cref="LoopRoundExtensions.BlockingCount"/>.</param>
internal readonly record struct LoopRank(int FailedReviewers, int BlockingFindings) : IComparable<LoopRank>
{
    /// <inheritdoc/>
    public int CompareTo(LoopRank other) =>
        FailedReviewers != other.FailedReviewers
            ? FailedReviewers.CompareTo(other.FailedReviewers)
            : BlockingFindings.CompareTo(other.BlockingFindings);

    public static bool operator <(LoopRank left, LoopRank right) => left.CompareTo(right) < 0;

    public static bool operator >(LoopRank left, LoopRank right) => left.CompareTo(right) > 0;
}

/// <summary>How the review loop compares its rounds when a D-085 safeguard is switched on.</summary>
internal static class LoopRoundExtensions
{
    /// <summary>
    /// The blocking findings of a round: every finding of a reviewer that did not fail and is at or above
    /// <paramref name="threshold"/>, plus every finding of a reviewer that rejects. Findings of failed reviewers describe an outage,
    /// not the text, and are never blocking here.
    /// </summary>
    public static IReadOnlyList<Finding> BlockingFindings(this EvaluationAggregate aggregate, FindingSeverity threshold) =>
        aggregate.Reviews
            .Where(review => review.Decision != ReviewDecision.Failed)
            .SelectMany(review => review.Findings.Where(finding => review.Decision.Blocks() || finding.Severity >= threshold))
            .ToList();

    /// <summary>
    /// The number of <see cref="BlockingFindings"/> plus one for each rejecting reviewer without any finding. It is 0 exactly when the
    /// loop's policy (failed reviewers not blocking, <paramref name="threshold"/> as its blocking severity) approves the round.
    /// </summary>
    public static int BlockingCount(this EvaluationAggregate aggregate, FindingSeverity threshold) =>
        aggregate.BlockingFindings(threshold).Count
        + aggregate.Reviews.Count(review => review.Decision.Blocks() && review.Findings.Count == 0);

    /// <summary>The <see cref="LoopRank"/> of a round.</summary>
    public static LoopRank Rank(this EvaluationAggregate aggregate, FindingSeverity threshold) =>
        new(aggregate.Reviews.Count(review => review.Decision == ReviewDecision.Failed), aggregate.BlockingCount(threshold));

    /// <summary>The round with the smallest <see cref="LoopRank"/>, the most recent one on a tie; <c>null</c> without rounds.</summary>
    public static IterationRecord? BestRound(this IterationHistory history, FindingSeverity threshold) =>
        history.Records
            .OrderBy(record => record.EvaluationResult.Rank(threshold))
            .ThenByDescending(record => record.Iteration)
            .FirstOrDefault();

    /// <summary>
    /// True when the last two rounds both brought no progress, that is neither ranks strictly better than every round before it.
    /// Needs at least three rounds.
    /// </summary>
    public static bool LastTwoRoundsWithoutProgress(this IterationHistory history, FindingSeverity threshold)
    {
        var ranks = history.Records.Select(record => record.EvaluationResult.Rank(threshold)).ToList();
        return ranks.Count >= 3 && !ranks.ProgressAt(ranks.Count - 1) && !ranks.ProgressAt(ranks.Count - 2);
    }

    private static bool ProgressAt(this List<LoopRank> ranks, int index) =>
        ranks.Take(index).All(earlier => ranks[index] < earlier);

    private static bool Blocks(this ReviewDecision decision) =>
        decision is ReviewDecision.Rejected or ReviewDecision.RetrySuggested;
}
