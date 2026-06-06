using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Tests.Llm;
using Geef.Sdk.Context;
using Geef.Sdk.Results;

namespace Geef.Atelier.Tests.Pipeline;

public sealed class ProfileBasedReviewerMandatoryFindingsTests
{
    private static ReviewerProfile TestProfile() => new(
        Name: "test-reviewer",
        DisplayName: "Test",
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

    // ── No retry cases ─────────────────────────────────────────────────────

    [Fact]
    public async Task ApprovedWithFindings_NoRetry_FindingsPreserved()
    {
        var client   = new SequencedLlmClient(ApprovedWithOneInfoFinding);
        var resolver = new TestLlmClientResolver(client);
        var reviewer = new ProfileBasedReviewer(TestProfile(), resolver);

        var result = await reviewer.ReviewAsync(BuildContext(), CancellationToken.None);

        // Severity-based: a single non-blocking (info) finding ⇒ ApprovedWithWarnings, no retry.
        Assert.Equal(ReviewDecision.ApprovedWithWarnings, result.Decision);
        Assert.Single(result.Findings);
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task RejectedFlagWithZeroFindings_IsApprovedAndRetried()
    {
        // Convergence is severity-based: a review with NO critical/major finding never rejects, even
        // when the model sets approved=false. Zero findings is a shallow review ⇒ one retry; if the
        // retry is still empty, the result is Approved with no findings (cannot block on nothing).
        var client   = new SequencedLlmClient(RejectedWithZeroFindings, RejectedWithZeroFindings);
        var resolver = new TestLlmClientResolver(client);
        var reviewer = new ProfileBasedReviewer(TestProfile(), resolver);

        var result = await reviewer.ReviewAsync(BuildContext(), CancellationToken.None);

        Assert.Equal(ReviewDecision.Approved, result.Decision);
        Assert.Empty(result.Findings);
        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task MajorFinding_IsRejected_RegardlessOfApprovedFlag()
    {
        // Even if the model sets approved=true, a major finding blocks the loop.
        var client   = new SequencedLlmClient(ApprovedTrueWithMajorFinding);
        var resolver = new TestLlmClientResolver(client);
        var reviewer = new ProfileBasedReviewer(TestProfile(), resolver);

        var result = await reviewer.ReviewAsync(BuildContext(), CancellationToken.None);

        Assert.Equal(ReviewDecision.Rejected, result.Decision);
        Assert.Single(result.Findings);
        Assert.Equal(1, client.CallCount);
    }

    // ── Retry cases ────────────────────────────────────────────────────────

    [Fact]
    public async Task ApprovedWithZeroFindings_TriggersRetry()
    {
        var client   = new SequencedLlmClient(ApprovedWithZeroFindings, ApprovedWithOneInfoFinding);
        var resolver = new TestLlmClientResolver(client);
        var reviewer = new ProfileBasedReviewer(TestProfile(), resolver);

        await reviewer.ReviewAsync(BuildContext(), CancellationToken.None);

        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task ApprovedWithZeroFindings_RetrySucceeds_FindingsFromRetryReturned()
    {
        var client   = new SequencedLlmClient(ApprovedWithZeroFindings, ApprovedWithOneInfoFinding);
        var resolver = new TestLlmClientResolver(client);
        var reviewer = new ProfileBasedReviewer(TestProfile(), resolver);

        var result = await reviewer.ReviewAsync(BuildContext(), CancellationToken.None);

        // Retry yields one info finding ⇒ ApprovedWithWarnings (non-blocking).
        Assert.Equal(ReviewDecision.ApprovedWithWarnings, result.Decision);
        Assert.Single(result.Findings);
        Assert.Equal(FindingSeverity.Info, result.Findings[0].Severity);
    }

    [Fact]
    public async Task ApprovedWithZeroFindings_RetryAlsoEmpty_ResultHasZeroFindings()
    {
        // When both calls return zero findings, return the result without failing.
        var client   = new SequencedLlmClient(ApprovedWithZeroFindings, ApprovedWithZeroFindings);
        var resolver = new TestLlmClientResolver(client);
        var reviewer = new ProfileBasedReviewer(TestProfile(), resolver);

        var result = await reviewer.ReviewAsync(BuildContext(), CancellationToken.None);

        Assert.Equal(ReviewDecision.Approved, result.Decision);
        Assert.Empty(result.Findings);
        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task ApprovedWithZeroFindings_RetryPromptContainsMandatoryFindingsReminder()
    {
        var client   = new CapturingSequencedLlmClient(ApprovedWithZeroFindings, ApprovedWithOneInfoFinding);
        var resolver = new TestLlmClientResolver(client);
        var reviewer = new ProfileBasedReviewer(TestProfile(), resolver);

        await reviewer.ReviewAsync(BuildContext(), CancellationToken.None);

        Assert.Equal(2, client.Requests.Count);
        var retryPrompt = client.Requests[1].UserPrompt;
        Assert.Contains("zero findings", retryPrompt);
        Assert.Contains("submit_review", retryPrompt);
        // Retry prompt extends the original prompt.
        Assert.Contains(client.Requests[0].UserPrompt, retryPrompt);
    }

    [Fact]
    public async Task ApprovedWithZeroFindings_RetryFailsToolCall_FallsBackToFirstResult()
    {
        var client   = new SequencedLlmClient(ApprovedWithZeroFindings, StopResponse);
        var resolver = new TestLlmClientResolver(client);
        var reviewer = new ProfileBasedReviewer(TestProfile(), resolver);

        var result = await reviewer.ReviewAsync(BuildContext(), CancellationToken.None);

        // First result (approved, 0 findings) is returned when retry can't parse tool call.
        Assert.Equal(ReviewDecision.Approved, result.Decision);
        Assert.Empty(result.Findings);
        Assert.Equal(2, client.CallCount);
    }

    // ── Factory responses ──────────────────────────────────────────────────

    private static LlmResponse ApprovedWithZeroFindings => MakeToolResponse(
        """{"approved":true,"findings":[]}""");

    private static LlmResponse ApprovedWithOneInfoFinding => MakeToolResponse(
        """{"approved":true,"findings":[{"severity":"info","message":"Overall well-structured text."}]}""");

    private static LlmResponse RejectedWithZeroFindings => MakeToolResponse(
        """{"approved":false,"findings":[]}""");

    private static LlmResponse ApprovedTrueWithMajorFinding => MakeToolResponse(
        """{"approved":true,"findings":[{"severity":"major","message":"Missing a required section."}]}""");

    private static LlmResponse StopResponse => new()
    {
        Text         = "The text looks good.",
        FinishReason = "stop",
        TokenUsage   = new LlmTokenUsage { InputTokens = 5, OutputTokens = 5 }
    };

    private static LlmResponse MakeToolResponse(string json) => new()
    {
        Text              = "",
        FinishReason      = "tool_calls",
        ToolName          = "submit_review",
        ToolArgumentsJson = json,
        TokenUsage        = new LlmTokenUsage { InputTokens = 10, OutputTokens = 10 }
    };
}

// ── Test helpers ───────────────────────────────────────────────────────────

internal sealed class SequencedLlmClient(params LlmResponse[] responses) : ILlmClient
{
    public int CallCount { get; private set; }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var idx = Math.Min(CallCount, responses.Length - 1);
        CallCount++;
        return Task.FromResult(responses[idx]);
    }
}

internal sealed class CapturingSequencedLlmClient(params LlmResponse[] responses) : ILlmClient
{
    private readonly LlmResponse[] _responses = responses;
    public List<LlmRequest> Requests { get; } = [];

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        var idx = Math.Min(Requests.Count - 1, _responses.Length - 1);
        return Task.FromResult(_responses[idx]);
    }
}
