using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Infrastructure.Pipeline.Direct;
using Geef.Sdk.Context;
using Geef.Sdk.Results;

namespace Geef.Atelier.Tests.Pipeline.Direct;

public sealed class DeterministicChecksReviewerTests
{
    private static Task<ReviewResult> Review(string draft, OrderConstraints? constraints = null)
    {
        IRunContext context = new RunContext().Set(AtelierContextKeys.CurrentDraft, draft);
        if (constraints is not null)
            context = context.Set(AtelierContextKeys.OrderConstraints, constraints);
        return new DeterministicChecksReviewer().ReviewAsync(context, CancellationToken.None);
    }

    [Fact]
    public async Task A_violation_is_rejected_with_an_error_finding()
    {
        var constraints = OrderConstraints.None with { Length = new LengthConstraint(LengthUnit.Words, null, 5, "max. 5 Wörter", LengthKind.AtMost) };

        var result = await Review("Das sind deutlich mehr als fünf Wörter in diesem Satz hier.", constraints);

        Assert.Equal(ReviewDecision.Rejected, result.Decision);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(FindingSeverity.Error, finding.Severity);
        Assert.Equal(DeterministicChecksReviewer.FindingCategory, finding.Category);
        Assert.Equal(DeterministicChecksReviewer.ReviewerName, finding.ReviewerName);
        Assert.StartsWith("deterministic-checks:", finding.Fingerprint);
    }

    [Fact]
    public async Task A_clean_draft_is_approved_without_findings()
    {
        var result = await Review("Ein sauberer Text ohne jede Auffälligkeit, der einfach passt.");

        Assert.Equal(ReviewDecision.Approved, result.Decision);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task Allowed_placeholders_are_info_and_still_approved()
    {
        var result = await Review("Sehr geehrte [Name], die Lieferung kommt am [Lieferdatum] bei Ihnen an.");

        Assert.Equal(ReviewDecision.Approved, result.Decision);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(FindingSeverity.Info, finding.Severity);
    }

    [Fact]
    public async Task The_fingerprint_is_stable_when_only_the_count_changes()
    {
        var constraints = OrderConstraints.None with { Length = new LengthConstraint(LengthUnit.Words, null, 5, "max. 5 Wörter", LengthKind.AtMost) };

        var first = await Review("Das sind deutlich mehr als fünf Wörter in diesem Satz hier.", constraints);
        var second = await Review("Das sind deutlich mehr als fünf Wörter in diesem Satz hier und noch mehr.", constraints);

        Assert.Equal(first.Findings[0].Fingerprint, second.Findings[0].Fingerprint);
        Assert.NotEqual(first.Findings[0].Message, second.Findings[0].Message);
    }

    [Fact]
    public async Task Without_a_draft_the_empty_check_rejects()
    {
        var result = await new DeterministicChecksReviewer().ReviewAsync(new RunContext(), CancellationToken.None);

        Assert.Equal(ReviewDecision.Rejected, result.Decision);
    }
}
