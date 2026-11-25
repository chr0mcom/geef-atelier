using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Infrastructure.Pipeline.Direct;
using Geef.Atelier.Infrastructure.Pipeline.Direct.FactCheck;
using Geef.Sdk.Results;
using Geef.Sdk.Runtime;

namespace Geef.Atelier.Tests.Pipeline.Direct.FactCheck;

/// <summary>
/// Which version is delivered after two rounds with two reviewers (contract stage 3, 4.3): no own selection, the SDK rule (fewest
/// rejections, newest on a tie) as mirrored by <see cref="DeliveredOutcomeExtensions.BestEffortIteration"/>.
/// </summary>
public sealed class DirectDeliveryRuleTests
{
    private static IterationRecord Record(int iteration, ReviewDecision checks, ReviewDecision facts) => new()
    {
        Iteration = iteration,
        StartedAt = DateTimeOffset.UtcNow,
        ExecutionDuration = TimeSpan.Zero,
        EvaluationResult = new EvaluationAggregate
        {
            Reviews =
            [
                new ReviewResult { ReviewerName = DeterministicChecksReviewer.ReviewerName, Decision = checks },
                new ReviewResult { ReviewerName = SourceFactCheckReviewer.ReviewerName, Decision = facts },
            ],
        },
    };

    private static int? Delivered(params IterationRecord[] records)
    {
        var history = new IterationHistory();
        foreach (var record in records)
            history.Add(record);
        return history.BestEffortIteration();
    }

    [Fact]
    public void Round_two_worse_in_both_reviewers_delivers_round_one() =>
        Assert.Equal(1, Delivered(
            Record(1, ReviewDecision.Approved, ReviewDecision.Rejected),
            Record(2, ReviewDecision.Rejected, ReviewDecision.Rejected)));

    [Fact]
    public void A_tie_with_facts_fixed_and_the_length_missed_delivers_round_two() =>
        Assert.Equal(2, Delivered(
            Record(1, ReviewDecision.Approved, ReviewDecision.Rejected),
            Record(2, ReviewDecision.Rejected, ReviewDecision.Approved)));

    [Fact]
    public void Round_two_better_delivers_round_two() =>
        Assert.Equal(2, Delivered(
            Record(1, ReviewDecision.Rejected, ReviewDecision.Rejected),
            Record(2, ReviewDecision.Approved, ReviewDecision.Rejected)));

    [Fact]
    public void A_failed_fact_check_in_round_two_does_not_count_as_a_rejection() =>
        Assert.Equal(2, Delivered(
            Record(1, ReviewDecision.Approved, ReviewDecision.Rejected),
            Record(2, ReviewDecision.Approved, ReviewDecision.Failed)));
}
