using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Sdk;
using Geef.Sdk.Context;

namespace Geef.Atelier.Tests.Infrastructure.Pipeline;

public sealed class ProfileBasedExecutorSeedDraftTests
{
    [Fact]
    public async Task RunAsync_Iteration1_WithSeedDraft_IncludesPreviousDraftInPrompt()
    {
        var captured  = new CapturingLlmClient();
        var executor  = MakeExecutor(captured);
        var context   = MakeContext(iter: 1, seedDraft: "original draft text");

        await executor.RunAsync(context, CancellationToken.None);

        Assert.Contains("original draft text", captured.LastRequest!.UserPrompt);
        Assert.Contains("interrupted run", captured.LastRequest.UserPrompt);
    }

    [Fact]
    public async Task RunAsync_Iteration1_WithoutSeedDraft_UsesNormalWritePrompt()
    {
        var captured = new CapturingLlmClient();
        var executor = MakeExecutor(captured);
        var context  = MakeContext(iter: 1, seedDraft: null);

        await executor.RunAsync(context, CancellationToken.None);

        Assert.DoesNotContain("interrupted run", captured.LastRequest!.UserPrompt);
        Assert.Contains("Write a text", captured.LastRequest.UserPrompt);
    }

    [Fact]
    public async Task RunAsync_Iteration2_WithSeedDraftInContext_IgnoresSeedDraft()
    {
        var captured = new CapturingLlmClient("iter-2-output");
        var executor = MakeExecutor(captured);
        // On iter 2 the seed draft is still in context — should not affect prompt.
        var context  = MakeContext(iter: 2, seedDraft: "seed from resume", currentDraft: "iter 1 output");

        await executor.RunAsync(context, CancellationToken.None);

        Assert.DoesNotContain("interrupted run", captured.LastRequest!.UserPrompt);
        Assert.DoesNotContain("seed from resume", captured.LastRequest.UserPrompt);
        Assert.Contains("iter 1 output", captured.LastRequest.UserPrompt);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static ProfileBasedExecutor MakeExecutor(ILlmClient client)
    {
        var profile = new ExecutorProfile(
            Name: "test", DisplayName: "Test", Description: "Test executor.",
            SystemPrompt: "You are a writer.", Provider: "test-provider",
            Model: "test-model", MaxTokens: 1000, IsSystem: false);

        return new ProfileBasedExecutor(profile, new StubResolver(client));
    }

    private static IRunContext MakeContext(int iter, string? seedDraft, string? currentDraft = null)
    {
        var ctx = new RunContext()
            .Set(AtelierContextKeys.GroundedBrief, "the briefing")
            .Set(GeefKeys.CurrentIteration, iter);

        if (seedDraft is not null)
            ctx = ctx.Set(AtelierContextKeys.SeedDraft, seedDraft);

        if (currentDraft is not null)
            ctx = ctx.Set(AtelierContextKeys.CurrentDraft, currentDraft);

        return ctx;
    }

    // ── Fakes ─────────────────────────────────────────────────────────────

    private sealed class CapturingLlmClient(string responseText = "generated text") : ILlmClient
    {
        public LlmRequest? LastRequest { get; private set; }

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new LlmResponse
            {
                Text         = responseText,
                TokenUsage   = new LlmTokenUsage { InputTokens = 10, OutputTokens = 5 },
                FinishReason = "stop"
            });
        }
    }

    private sealed class StubResolver(ILlmClient client) : ILlmClientResolver
    {
        public (ILlmClient Client, string Model, int MaxTokens) ForActor(string actorName)
            => (client, "test-model", 1000);

        public (ILlmClient Client, string Model, int MaxTokens) ForProfile(string provider, string model, int? maxTokens)
            => (client, model, maxTokens ?? 1000);
    }
}
