using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Sdk;
using Geef.Sdk.Context;

namespace Geef.Atelier.Tests.Infrastructure.Pipeline;

/// <summary>
/// Verifies that ProfileBasedExecutor activates document mode (DocumentMode=true, Document=prevDraft)
/// for CLI providers and leaves it off for API providers.
/// </summary>
public sealed class ProfileBasedExecutorDocumentModeTests
{
    [Fact]
    public async Task RunAsync_WithCliProvider_FirstIteration_SetsDocumentModeWithEmptyDocument()
    {
        // Iteration 1 without seed draft — document mode active, Document is empty string.
        var captured = new CapturingLlmClient("first draft text");
        var executor = MakeExecutor(captured, provider: "claude-cli");
        var context  = new RunContext()
            .Set(AtelierContextKeys.GroundedBrief, "the brief")
            .Set(GeefKeys.CurrentIteration, 1);

        await executor.RunAsync(context, CancellationToken.None);

        Assert.Single(captured.Requests);
        Assert.True(captured.Requests[0].DocumentMode, "CLI provider must enable document mode");
        Assert.Equal("", captured.Requests[0].Document);
    }

    [Fact]
    public async Task RunAsync_WithCliProvider_FirstIterationWithSeed_SetsDocumentModeWithSeedAsDocument()
    {
        const string seed = "seed draft content";
        var captured = new CapturingLlmClient("improved draft");
        var executor = MakeExecutor(captured, provider: "claude-cli");
        var context  = new RunContext()
            .Set(AtelierContextKeys.GroundedBrief, "the brief")
            .Set(AtelierContextKeys.SeedDraft, seed)
            .Set(GeefKeys.CurrentIteration, 1);

        await executor.RunAsync(context, CancellationToken.None);

        Assert.Single(captured.Requests);
        Assert.True(captured.Requests[0].DocumentMode);
        Assert.Equal(seed, captured.Requests[0].Document);
    }

    [Fact]
    public async Task RunAsync_WithCliProvider_RevisionIteration_SendsPrevDraftAsDocument()
    {
        // Iteration 2+: Document must be the previous draft, not null, so the CLI can edit it.
        const string prevDraft = "previous full document content";
        var captured = new CapturingLlmClient(new string('x', 50_000));
        var executor = MakeExecutor(captured, provider: "claude-cli");
        var context  = new RunContext()
            .Set(AtelierContextKeys.GroundedBrief, "the brief")
            .Set(AtelierContextKeys.CurrentDraft, prevDraft)
            .Set(GeefKeys.CurrentIteration, 2);

        await executor.RunAsync(context, CancellationToken.None);

        Assert.True(captured.Requests[0].DocumentMode);
        Assert.Equal(prevDraft, captured.Requests[0].Document);
    }

    [Fact]
    public async Task RunAsync_WithApiProvider_DoesNotSetDocumentMode()
    {
        // API providers (openrouter etc.) must NOT receive document_mode — they would reject the field.
        var captured = new CapturingLlmClient("api response text");
        var executor = MakeExecutor(captured, provider: "openrouter");
        var context  = new RunContext()
            .Set(AtelierContextKeys.GroundedBrief, "the brief")
            .Set(GeefKeys.CurrentIteration, 1);

        await executor.RunAsync(context, CancellationToken.None);

        Assert.Single(captured.Requests);
        Assert.False(captured.Requests[0].DocumentMode, "API provider must NOT enable document mode");
        Assert.Null(captured.Requests[0].Document);
    }

    [Fact]
    public async Task RunAsync_WithCodexCliProvider_SetsDocumentMode()
    {
        var captured = new CapturingLlmClient("codex first draft");
        var executor = MakeExecutor(captured, provider: "codex-cli");
        var context  = new RunContext()
            .Set(AtelierContextKeys.GroundedBrief, "the brief")
            .Set(GeefKeys.CurrentIteration, 1);

        await executor.RunAsync(context, CancellationToken.None);

        Assert.True(captured.Requests[0].DocumentMode);
    }

    [Fact]
    public async Task RunAsync_WithCliProvider_RevisionPrompt_DoesNotEmbedPrevDraft()
    {
        // In document mode, the revision prompt must NOT embed the full previous draft
        // (it's in draft.md already) — embedding it would double the context for large documents.
        const string prevDraft = "the previous full document";
        var captured = new CapturingLlmClient(new string('z', 50_000));
        var executor = MakeExecutor(captured, provider: "claude-cli");
        var context  = new RunContext()
            .Set(AtelierContextKeys.GroundedBrief, "briefing text")
            .Set(AtelierContextKeys.CurrentDraft, prevDraft)
            .Set(GeefKeys.CurrentIteration, 2);

        await executor.RunAsync(context, CancellationToken.None);

        var prompt = captured.Requests[0].UserPrompt;
        Assert.DoesNotContain(prevDraft, prompt);
        Assert.Contains("draft.md", prompt);
    }

    [Fact]
    public async Task RunAsync_WithCliProvider_ForcefulRetry_UsesFileEditEmphasis()
    {
        // When the regression guard fires in document mode, the retry prompt must use file-edit
        // language (write to draft.md) and must NOT use stdout-mode language
        // ("Output the ENTIRE document … NOTHING else"), which would make the retry a no-op.
        const string grounding = "GROUNDING: research for retry";
        var prevDraft = new string('p', 3000); // >= 2000 chars → regression guard active
        var captured  = new CapturingLlmClient(
            "short",               // first response: too short (<70% baseline) → triggers retry
            new string('r', 3000)); // retry response: full length → accepted
        var executor = MakeExecutor(captured, provider: "claude-cli");
        var context  = new RunContext()
            .Set(AtelierContextKeys.GroundedBrief, "the brief")
            .Set(AtelierContextKeys.GroundingContext, grounding)
            .Set(AtelierContextKeys.CurrentDraft, prevDraft)
            .Set(GeefKeys.CurrentIteration, 2);

        await executor.RunAsync(context, CancellationToken.None);

        Assert.Equal(2, captured.Requests.Count);
        var retryPrompt = captured.Requests[1].UserPrompt;
        Assert.Contains("draft.md", retryPrompt);
        Assert.DoesNotContain("Output the ENTIRE document", retryPrompt);
        Assert.DoesNotContain("NOTHING else", retryPrompt);
        // The retry must still carry the context out-of-band (not inlined into the prompt).
        Assert.Contains(grounding, captured.Requests[1].ContextDocument!);
        Assert.DoesNotContain(grounding, retryPrompt);
    }

    [Fact]
    public async Task RunAsync_WithCliProvider_SendsGroundingAndAdvisorAsContextDocument()
    {
        // In document mode grounding + advisor blocks must travel in ContextDocument, not the prompt,
        // so the proxy can offload them to context.md while findings stay in the argv prompt.
        const string grounding = "GROUNDING: web research results";
        const string advisor   = "ADVISOR: critical consultation";
        var captured = new CapturingLlmClient("first draft");
        var executor = MakeExecutor(captured, provider: "claude-cli");
        var context  = new RunContext()
            .Set(AtelierContextKeys.GroundedBrief, "the brief")
            .Set(AtelierContextKeys.GroundingContext, grounding)
            .Set(GeefKeys.AdvisorContext, advisor)
            .Set(GeefKeys.CurrentIteration, 1);

        await executor.RunAsync(context, CancellationToken.None);

        var req = captured.Requests[0];
        Assert.NotNull(req.ContextDocument);
        Assert.Contains(grounding, req.ContextDocument!);
        Assert.Contains(advisor, req.ContextDocument!);
        // Order must match the old inline PrependContextBlocks: advisor before grounding.
        Assert.True(
            req.ContextDocument!.IndexOf(advisor, StringComparison.Ordinal)
            < req.ContextDocument!.IndexOf(grounding, StringComparison.Ordinal),
            "advisor block must precede grounding block");
        Assert.DoesNotContain(grounding, req.UserPrompt);
        Assert.DoesNotContain(advisor, req.UserPrompt);
    }

    [Fact]
    public async Task RunAsync_WithCliProvider_NoContextBlocks_LeavesContextDocumentNull()
    {
        // No grounding/advisor present → ContextDocument must be null (not an empty string).
        var captured = new CapturingLlmClient("first draft");
        var executor = MakeExecutor(captured, provider: "claude-cli");
        var context  = new RunContext()
            .Set(AtelierContextKeys.GroundedBrief, "the brief")
            .Set(GeefKeys.CurrentIteration, 1);

        await executor.RunAsync(context, CancellationToken.None);

        Assert.Null(captured.Requests[0].ContextDocument);
    }

    [Fact]
    public async Task RunAsync_WithCliProvider_WhitespaceContextBlocks_LeavesContextDocumentNull()
    {
        // Whitespace-only blocks must not produce a near-empty ContextDocument.
        var captured = new CapturingLlmClient("first draft");
        var executor = MakeExecutor(captured, provider: "claude-cli");
        var context  = new RunContext()
            .Set(AtelierContextKeys.GroundedBrief, "the brief")
            .Set(AtelierContextKeys.GroundingContext, "   ")
            .Set(GeefKeys.AdvisorContext, "\n\t ")
            .Set(GeefKeys.CurrentIteration, 1);

        await executor.RunAsync(context, CancellationToken.None);

        Assert.Null(captured.Requests[0].ContextDocument);
    }

    [Fact]
    public async Task RunAsync_WithApiProvider_KeepsContextInlineAndContextDocumentNull()
    {
        // API providers keep the old inline behaviour: grounding/advisor in the prompt, no ContextDocument.
        const string grounding = "GROUNDING: web research results";
        const string advisor   = "ADVISOR: critical consultation";
        var captured = new CapturingLlmClient("api response");
        var executor = MakeExecutor(captured, provider: "openrouter");
        var context  = new RunContext()
            .Set(AtelierContextKeys.GroundedBrief, "the brief")
            .Set(AtelierContextKeys.GroundingContext, grounding)
            .Set(GeefKeys.AdvisorContext, advisor)
            .Set(GeefKeys.CurrentIteration, 1);

        await executor.RunAsync(context, CancellationToken.None);

        var req = captured.Requests[0];
        Assert.Null(req.ContextDocument);
        Assert.Contains(grounding, req.UserPrompt);
        Assert.Contains(advisor, req.UserPrompt);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static ProfileBasedExecutor MakeExecutor(ILlmClient client, string provider)
    {
        var profile = new ExecutorProfile(
            Name: "test", DisplayName: "Test", Description: "Test executor.",
            SystemPrompt: "You are a writer.", Provider: provider,
            Model: "test-model", MaxTokens: 64000, IsSystem: false);

        return new ProfileBasedExecutor(profile, new StubResolver(client));
    }

    private sealed class CapturingLlmClient(params string[] responses) : ILlmClient
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
    }
}
