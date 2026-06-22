using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Sdk;
using Geef.Sdk.Context;

namespace Geef.Atelier.Tests.Infrastructure.Pipeline;

/// <summary>
/// Guards against the executor returning a change-summary / cover letter instead of the full
/// revised document on revision iterations (observed length-collapse oscillation in production).
/// </summary>
public sealed class ProfileBasedExecutorRegressionGuardTests
{
    private static readonly string LongDraft = new('x', 50_000);

    [Fact]
    public async Task RunAsync_RevisionIteration_PromptForbidsChangeSummary()
    {
        var captured = new SequenceLlmClient(LongDraft);
        var executor = MakeExecutor(captured);
        var context  = MakeContext(iter: 2, currentDraft: LongDraft);

        await executor.RunAsync(context, CancellationToken.None);

        var prompt = captured.Requests[0].UserPrompt;
        Assert.Contains("COMPLETE", prompt);
        Assert.Contains("change-summary", prompt);
        Assert.Contains("standalone document", prompt);
    }

    [Fact]
    public async Task RunAsync_WhenResponseCollapses_RetriesOnceAndKeepsRecoveredFullDraft()
    {
        // First call returns a tiny changelog; the forceful retry returns the full document.
        var recovered = new string('y', 48_000);
        var captured  = new SequenceLlmClient("- §6.2 abgeschwächt zu ...", recovered);
        var executor  = MakeExecutor(captured);
        var context   = MakeContext(iter: 2, currentDraft: LongDraft);

        var result = await executor.RunAsync(context, CancellationToken.None);

        Assert.Equal(2, captured.Requests.Count);                       // one retry happened
        Assert.Contains("REJECTED", captured.Requests[1].UserPrompt);   // forceful retry prompt
        Assert.Equal(recovered, result.UpdatedContext.GetRequired(AtelierContextKeys.CurrentDraft));
    }

    [Fact]
    public async Task RunAsync_WhenBothAttemptsCollapse_FallsBackToPreviousFullDraft()
    {
        var captured = new SequenceLlmClient("tiny changelog A", "tiny changelog B");
        var executor = MakeExecutor(captured);
        var context  = MakeContext(iter: 2, currentDraft: LongDraft);

        var result = await executor.RunAsync(context, CancellationToken.None);

        Assert.Equal(2, captured.Requests.Count);
        // Never let a short changelog become the state — keep the comprehensive previous draft.
        Assert.Equal(LongDraft, result.UpdatedContext.GetRequired(AtelierContextKeys.CurrentDraft));
    }

    [Fact]
    public async Task RunAsync_WhenRevisionStaysSubstantial_DoesNotRetry()
    {
        var full     = new string('z', 49_000);
        var captured = new SequenceLlmClient(full);
        var executor = MakeExecutor(captured);
        var context  = MakeContext(iter: 2, currentDraft: LongDraft);

        var result = await executor.RunAsync(context, CancellationToken.None);

        Assert.Single(captured.Requests);
        Assert.Equal(full, result.UpdatedContext.GetRequired(AtelierContextKeys.CurrentDraft));
    }

    [Fact]
    public async Task RunAsync_WhenRevisionDropsBelow70Percent_TreatsPartialAsRegression()
    {
        // 27k vs a 50k previous draft = 54% — a partial document, above the old 50% bar but below 70%.
        var partial   = new string('p', 27_000);
        var recovered = new string('r', 49_000);
        var captured  = new SequenceLlmClient(partial, recovered);
        var executor  = MakeExecutor(captured);
        var context   = MakeContext(iter: 2, currentDraft: LongDraft);

        var result = await executor.RunAsync(context, CancellationToken.None);

        Assert.Equal(2, captured.Requests.Count);   // partial was caught and retried
        Assert.Equal(recovered, result.UpdatedContext.GetRequired(AtelierContextKeys.CurrentDraft));
    }

    [Fact]
    public async Task RunAsync_RegressionMeasuredAgainstBestDraft_NotMerelyPreviousDraft()
    {
        // The current draft is an already-accepted 36k partial, but the best draft ever seen is 50k.
        // A 30k response is 83% of the current draft (would pass) yet only 60% of the best (must fail).
        var thirtyK  = new string('a', 30_000);
        var recovered = new string('b', 48_000);
        var captured = new SequenceLlmClient(thirtyK, recovered);
        var executor = MakeExecutor(captured);
        var context  = new RunContext()
            .Set(AtelierContextKeys.GroundedBrief, "the briefing")
            .Set(GeefKeys.CurrentIteration, 3)
            .Set(AtelierContextKeys.CurrentDraft, new string('c', 36_000))
            .Set(AtelierContextKeys.BestDraft, LongDraft);

        await executor.RunAsync(context, CancellationToken.None);

        Assert.Equal(2, captured.Requests.Count);   // measured against the 50k best, so 30k is a regression
    }

    [Fact]
    public async Task RunAsync_WhenPreviousDraftIsShort_DoesNotTreatShorterAsRegression()
    {
        // Short previous drafts are below the comparable threshold — no false-positive retry.
        var captured = new SequenceLlmClient("ok");
        var executor = MakeExecutor(captured);
        var context  = MakeContext(iter: 2, currentDraft: "short prev draft");

        await executor.RunAsync(context, CancellationToken.None);

        Assert.Single(captured.Requests);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static ProfileBasedExecutor MakeExecutor(ILlmClient client)
    {
        var profile = new ExecutorProfile(
            Name: "test", DisplayName: "Test", Description: "Test executor.",
            SystemPrompt: "You are a writer.", Provider: "test-provider",
            Model: "test-model", MaxTokens: 64000, IsSystem: false);

        return new ProfileBasedExecutor(profile, new StubResolver(client));
    }

    private static IRunContext MakeContext(int iter, string currentDraft)
        => new RunContext()
            .Set(AtelierContextKeys.GroundedBrief, "the briefing")
            .Set(GeefKeys.CurrentIteration, iter)
            .Set(AtelierContextKeys.CurrentDraft, currentDraft);

    private sealed class SequenceLlmClient(params string[] responses) : ILlmClient
    {
        private int _i;
        public List<LlmRequest> Requests { get; } = [];

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var text = responses[Math.Min(_i, responses.Length - 1)];
            _i++;
            return Task.FromResult(new LlmResponse
            {
                Text         = text,
                TokenUsage   = new LlmTokenUsage { InputTokens = 10, OutputTokens = 5 },
                FinishReason = "stop"
            });
        }
    }

    private sealed class StubResolver(ILlmClient client) : ILlmClientResolver
    {
        public (ILlmClient Client, string Model, int MaxTokens) ForActor(string actorName)
            => (client, "test-model", 64000);

        public (ILlmClient Client, string Model, int MaxTokens) ForProfile(string provider, string model, int? maxTokens)
            => (client, model, maxTokens ?? 64000);

        public bool SupportsAgenticTools(string providerName) => true;
        public bool SupportsStructuredOutputs(string providerName) => true;
    }
}
