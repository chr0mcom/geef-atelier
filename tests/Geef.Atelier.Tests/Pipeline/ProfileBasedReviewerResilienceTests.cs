using System.Net;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Tests.Llm;
using Geef.Sdk.Context;

namespace Geef.Atelier.Tests.Pipeline;

/// <summary>
/// Verifies ProfileBasedReviewer's behavior around provider failures.
///
/// Since SDK v1.1.0 reviewer fault isolation lives in the SDK's InstrumentedReviewer:
/// a provider exception propagates out of ProfileBasedReviewer and is caught by
/// InstrumentedReviewer, which converts it to ReviewDecision.Failed (non-blocking when
/// FailedReviewerHandling=TreatAsNonBlocking, set by ConvergencePolicyBuilder).
/// ProfileBasedReviewer no longer silently swallows provider errors.
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
    public async Task PermanentProviderError_PropagatesAsHttpRequestException()
    {
        // 401 is a permanent error — LlmResilience does not retry it.
        // The exception propagates; SDK InstrumentedReviewer converts it to Failed.
        var resolver = new TestLlmClientResolver(ThrowingLlmClient.HttpError(HttpStatusCode.Unauthorized));
        var reviewer = new ProfileBasedReviewer(TestProfile(), resolver);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => reviewer.ReviewAsync(BuildContext(), CancellationToken.None));
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
