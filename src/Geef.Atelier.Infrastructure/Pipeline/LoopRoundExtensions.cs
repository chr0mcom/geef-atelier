using Geef.Sdk.Results;
using Geef.Sdk.Runtime;

namespace Geef.Atelier.Infrastructure.Pipeline;

/// <summary>
/// The rank of one review round (D-085), counted only over the review basis (<see cref="LoopRoundExtensions.ReviewBasis"/>): first
/// its critical findings, then all its blocking findings; smaller is better.
/// </summary>
/// <param name="CriticalFindings">Blocking findings of severity critical.</param>
/// <param name="BlockingFindings">The round's count from <see cref="LoopRoundExtensions.BlockingCount"/>, restricted to the basis.</param>
internal readonly record struct LoopRank(int CriticalFindings, int BlockingFindings) : IComparable<LoopRank>
{
    /// <inheritdoc/>
    public int CompareTo(LoopRank other) =>
        CriticalFindings != other.CriticalFindings
            ? CriticalFindings.CompareTo(other.CriticalFindings)
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
        aggregate.Reviews.BlockingOf(threshold);

    /// <summary>
    /// The number of <see cref="BlockingFindings"/> plus one for each rejecting reviewer without any finding. It is 0 exactly when the
    /// loop's policy (failed reviewers not blocking, <paramref name="threshold"/> as its blocking severity) approves the round.
    /// </summary>
    public static int BlockingCount(this EvaluationAggregate aggregate, FindingSeverity threshold) =>
        aggregate.Reviews.BlockingCountOf(threshold);

    /// <summary>
    /// The reviewers that answered in every one of <paramref name="rounds"/> (never <see cref="ReviewDecision.Failed"/>). Rounds are
    /// compared only over them, so a reviewer that failed in some round can neither make that round look better nor the others worse.
    /// </summary>
    public static IReadOnlySet<string> ReviewBasis(this IEnumerable<IterationRecord> rounds)
    {
        HashSet<string>? basis = null;
        foreach (var round in rounds)
        {
            var answered = round.EvaluationResult.Reviews
                .Where(review => review.Decision != ReviewDecision.Failed)
                .Select(review => review.ReviewerName);
            if (basis is null)
                basis = [.. answered];
            else
                basis.IntersectWith(answered);
        }
        return basis ?? [];
    }

    /// <summary>The <see cref="LoopRank"/> of a round over the reviewers in <paramref name="basis"/>.</summary>
    public static LoopRank Rank(this EvaluationAggregate aggregate, FindingSeverity threshold, IReadOnlySet<string> basis)
    {
        var reviews = aggregate.Reviews.Where(review => basis.Contains(review.ReviewerName)).ToList();
        return new(
            reviews.BlockingOf(threshold).Count(finding => finding.Severity == FindingSeverity.Critical),
            reviews.BlockingCountOf(threshold));
    }

    /// <summary>
    /// The round to deliver: among the selectable rounds (<paramref name="fallbackIteration"/>, the round the SDK picked, and every round
    /// whose text is not blank and at least <see cref="ProfileBasedExecutor.RegressionRatio"/> of the longest round text) the smallest
    /// <see cref="LoopRank"/> over their <see cref="ReviewBasis"/>, then the fewest failed reviewers, then the most recent. Without a
    /// common reviewer the fallback round; <c>null</c> without rounds.
    /// </summary>
    public static IterationRecord? BestRound(this IterationHistory history, FindingSeverity threshold, int fallbackIteration)
    {
        if (history.Records.Count == 0)
            return null;

        var longest = history.Records.Max(record => record.DraftLength());
        var candidates = history.Records
            .Where(record => record.Iteration == fallbackIteration || record.IsFullDraft(longest))
            .ToList();
        var basis = candidates.ReviewBasis();
        if (basis.Count == 0)
            return candidates.FirstOrDefault(record => record.Iteration == fallbackIteration);

        return candidates
            .OrderBy(record => record.EvaluationResult.Rank(threshold, basis))
            .ThenBy(record => record.EvaluationResult.FailedReviewers.Count)
            .ThenByDescending(record => record.Iteration)
            .FirstOrDefault();
    }

    /// <summary>
    /// True when the last two rounds both brought no progress, that is neither ranks strictly better than every round before it, compared
    /// over the <see cref="ReviewBasis"/> of all rounds. Needs at least three rounds and a reviewer that answered in all of them.
    /// </summary>
    public static bool LastTwoRoundsWithoutProgress(this IterationHistory history, FindingSeverity threshold)
    {
        var basis = history.Records.ReviewBasis();
        if (history.Records.Count < 3 || basis.Count == 0)
            return false;

        var ranks = history.Records.Select(record => record.EvaluationResult.Rank(threshold, basis)).ToList();
        return !ranks.ProgressAt(ranks.Count - 1) && !ranks.ProgressAt(ranks.Count - 2);
    }

    private static List<Finding> BlockingOf(this IEnumerable<ReviewResult> reviews, FindingSeverity threshold) =>
        reviews
            .Where(review => review.Decision != ReviewDecision.Failed)
            .SelectMany(review => review.Findings.Where(finding => review.Decision.Blocks() || finding.Severity >= threshold))
            .ToList();

    private static int BlockingCountOf(this IReadOnlyCollection<ReviewResult> reviews, FindingSeverity threshold) =>
        reviews.BlockingOf(threshold).Count
        + reviews.Count(review => review.Decision.Blocks() && review.Findings.Count == 0);

    private static int DraftLength(this IterationRecord record) =>
        record.Context is not null
        && record.Context.TryGet(AtelierContextKeys.CurrentDraft, out var draft)
        && !string.IsNullOrWhiteSpace(draft)
            ? draft.Length
            : 0;

    private static bool IsFullDraft(this IterationRecord record, int longest)
    {
        var length = record.DraftLength();
        return length > 0 && length >= longest * ProfileBasedExecutor.RegressionRatio;
    }

    private static bool ProgressAt(this List<LoopRank> ranks, int index) =>
        ranks.Take(index).All(earlier => ranks[index] < earlier);

    private static bool Blocks(this ReviewDecision decision) =>
        decision is ReviewDecision.Rejected or ReviewDecision.RetrySuggested;
}
