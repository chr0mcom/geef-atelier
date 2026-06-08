using System.Net;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Tests.Llm;
using Geef.Sdk.Context;
using Geef.Sdk.Results;

namespace Geef.Atelier.Tests.Pipeline;

/// <summary>
/// Verifies that a reviewer whose LLM provider is unavailable never aborts the run: it is skipped
/// for the round and reports a single non-blocking <see cref="FindingSeverity.Info"/> finding.
/// </summary>
public sealed class ProfileBasedReviewerResilienceTests
{
    private static ReviewerProfile TestProfile() => new(
        Name: "test-reviewer",
        DisplayName: "Test Reviewer",
        Description: "Test reviewer",
        SystemPrompt: "You are a reviewer.",
        Provider: "test",
        Model: "test-model",
        MaxTokens: null,
        IsSystem: false);

    private static IRunContext BuildContext() =>
        new RunContext()
            .Set(AtelierContextKeys.GroundedBrief, "Briefing text.")
            .Set(AtelierContextKeys.CurrentDraft,  "Draft text.")
            .Set(GeefKeys.CurrentIteration,        1);

    [Fact]
    public async Task ProviderUnavailable_SkipsReviewer_WithNonBlockingInfoFinding()
    {
        // 401 is a permanent error → no retries → the skip path runs instantly.
        var resolver = new TestLlmClientResolver(ThrowingLlmClient.HttpError(HttpStatusCode.Unauthorized));
        var reviewer = new ProfileBasedReviewer(TestProfile(), resolver);

        var result = await reviewer.ReviewAsync(BuildContext(), CancellationToken.None);

        // Non-blocking: the run keeps converging on the remaining reviewers.
        Assert.Equal(ReviewDecision.ApprovedWithWarnings, result.Decision);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(FindingSeverity.Info, finding.Severity);
        Assert.Contains("unavailable", finding.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProviderUnavailable_DoesNotThrow()
    {
        var resolver = new TestLlmClientResolver(ThrowingLlmClient.HttpError(HttpStatusCode.Unauthorized));
        var reviewer = new ProfileBasedReviewer(TestProfile(), resolver);

        // Must complete normally rather than propagating the provider exception.
        var result = await reviewer.ReviewAsync(BuildContext(), CancellationToken.None);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task RunCancellation_Propagates_AndIsNotMaskedAsSkip()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var resolver = new TestLlmClientResolver(ThrowingLlmClient.Timeout());
        var reviewer = new ProfileBasedReviewer(TestProfile(), resolver);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reviewer.ReviewAsync(BuildContext(), cts.Token));
    }
}
