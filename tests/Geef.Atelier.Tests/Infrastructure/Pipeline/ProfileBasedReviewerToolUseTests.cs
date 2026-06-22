using Geef.Atelier.Application.Tools;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Core.Persistence.Tools;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Tests.Llm;
using Geef.Sdk.Context;
using System.Text.Json;

namespace Geef.Atelier.Tests.Infrastructure.Pipeline;

/// <summary>
/// Unit tests verifying that <see cref="ProfileBasedReviewer"/> correctly routes between
/// the agentic tool-use loop path and the legacy single-shot path based on profile.ToolNames
/// and provider capability.
/// </summary>
public sealed class ProfileBasedReviewerToolUseTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static ReviewerProfile ProfileWithTools(string provider = "openrouter") => new(
        Name: "test-reviewer",
        DisplayName: "Test",
        Description: "Test reviewer",
        SystemPrompt: "You are a reviewer.",
        Provider: provider,
        Model: "test-model",
        MaxTokens: null,
        IsSystem: false,
        ToolNames: new[] { "search-tool" });

    private static ReviewerProfile ProfileWithoutTools() => new(
        Name: "test-reviewer",
        DisplayName: "Test",
        Description: "Test reviewer",
        SystemPrompt: "You are a reviewer.",
        Provider: "openrouter",
        Model: "test-model",
        MaxTokens: null,
        IsSystem: false,
        ToolNames: null);

    private static IRunContext BuildContext() =>
        new RunContext()
            .Set(AtelierContextKeys.GroundedBrief, "Briefing text.")
            .Set(AtelierContextKeys.CurrentDraft, "Draft text.")
            .Set(GeefKeys.CurrentIteration, 1);

    private static ToolDefinition MakeTool(string name = "search-tool") =>
        new(
            Name: name,
            DisplayName: "Search Tool",
            Description: "Searches for information.",
            ToolType: ToolType.StaticContext,
            Settings: new Dictionary<string, string> { ["StaticContent"] = "result" },
            SecretRef: null,
            LlmSchema: JsonDocument.Parse(@"{""type"":""object"",""properties"":{}}").RootElement,
            AccessClass: ToolAccessClass.ReadOnly,
            IsSystem: false);

    // -------------------------------------------------------------------------
    // Test: profile with tool names + capable provider → uses tool loop
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ReviewAsync_WithToolNames_CapableProvider_UsesToolLoop()
    {
        // Arrange: the tool loop runner returns a submit_review result.
        var submitReviewArgs = """{"approved":true,"findings":[{"severity":"minor","message":"Structure could be tighter."}]}""";
        var fakeRunner = new FakeToolUseRunner(new ToolLoopResult
        {
            FinalText = submitReviewArgs,
            EndReason = ToolLoopEndReason.RequiredToolCalled,
            ToolCallCount = 1,
            TotalTokenUsage = new LlmTokenUsage { InputTokens = 10, OutputTokens = 10 }
        });

        var fakeRepo = new FakeToolDefinitionRepository(MakeTool("search-tool"));
        // TestLlmClientResolver returns SupportsAgenticTools=true by default.
        var resolver = new TestLlmClientResolver(new ToolUseTestLlmClient(/* no fallback responses needed */));

        var reviewer = new ProfileBasedReviewer(
            ProfileWithTools(),
            resolver,
            pricingCatalog: null,
            costAccumulator: null,
            toolUseRunner: fakeRunner,
            toolDefinitionRepository: fakeRepo);

        // Act
        var result = await reviewer.ReviewAsync(BuildContext(), CancellationToken.None);

        // Assert: the loop was invoked and the result was parsed from the loop's FinalText.
        Assert.True(fakeRunner.WasCalled);
        Assert.Equal("submit_review", fakeRunner.LastRequiredFinalTool);
        Assert.Equal(Geef.Sdk.Results.ReviewDecision.ApprovedWithWarnings, result.Decision);
        Assert.Single(result.Findings);
        Assert.Contains("Structure could be tighter", result.Findings[0].Message);
    }

    // -------------------------------------------------------------------------
    // Test: profile with tool names + INCAPABLE provider → single-shot fallback
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ReviewAsync_WithToolNames_IncapableProvider_FallsBackToSingleShot()
    {
        // Arrange: a resolver that reports SupportsAgenticTools=false for any provider.
        var singleShotResponse = MakeToolResponse(
            """{"approved":true,"findings":[{"severity":"info","message":"Looks good overall."}]}""");
        var client = new ToolUseTestLlmClient(singleShotResponse);
        var resolver = new NonAgenticTestLlmClientResolver(client);

        var fakeRunner = new FakeToolUseRunner(new ToolLoopResult
        {
            FinalText = "",
            EndReason = ToolLoopEndReason.FinalText,
            ToolCallCount = 0,
            TotalTokenUsage = new LlmTokenUsage { InputTokens = 0, OutputTokens = 0 }
        });
        var fakeRepo = new FakeToolDefinitionRepository(MakeTool("search-tool"));

        var reviewer = new ProfileBasedReviewer(
            ProfileWithTools("incapable-provider"),
            resolver,
            toolUseRunner: fakeRunner,
            toolDefinitionRepository: fakeRepo);

        // Act
        var result = await reviewer.ReviewAsync(BuildContext(), CancellationToken.None);

        // Assert: the loop runner was NOT called; single-shot path used.
        Assert.False(fakeRunner.WasCalled);
        Assert.Equal(1, client.CallCount);
        Assert.Equal(Geef.Sdk.Results.ReviewDecision.ApprovedWithWarnings, result.Decision);
    }

    // -------------------------------------------------------------------------
    // Test: profile without tool names → single-shot path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ReviewAsync_WithoutToolNames_UsesSingleShotPath()
    {
        // Arrange: no tools on the profile → single-shot is the only path.
        var singleShotResponse = MakeToolResponse(
            """{"approved":true,"findings":[{"severity":"major","message":"Missing key section."}]}""");
        var client = new ToolUseTestLlmClient(singleShotResponse);
        var resolver = new TestLlmClientResolver(client);

        var fakeRunner = new FakeToolUseRunner(new ToolLoopResult
        {
            FinalText = "",
            EndReason = ToolLoopEndReason.FinalText,
            ToolCallCount = 0,
            TotalTokenUsage = new LlmTokenUsage { InputTokens = 0, OutputTokens = 0 }
        });
        var fakeRepo = new FakeToolDefinitionRepository(MakeTool("search-tool"));

        var reviewer = new ProfileBasedReviewer(
            ProfileWithoutTools(),
            resolver,
            toolUseRunner: fakeRunner,
            toolDefinitionRepository: fakeRepo);

        // Act
        var result = await reviewer.ReviewAsync(BuildContext(), CancellationToken.None);

        // Assert: the loop runner was NOT called despite being wired in.
        Assert.False(fakeRunner.WasCalled);
        Assert.Equal(1, client.CallCount);
        Assert.Equal(Geef.Sdk.Results.ReviewDecision.ApprovedWithWarnings, result.Decision);
        Assert.Single(result.Findings);
    }

    // -------------------------------------------------------------------------
    // Factory helpers
    // -------------------------------------------------------------------------

    private static LlmResponse MakeToolResponse(string json) => new()
    {
        Text = "",
        FinishReason = "tool_calls",
        ToolName = "submit_review",
        ToolArgumentsJson = json,
        TokenUsage = new LlmTokenUsage { InputTokens = 10, OutputTokens = 10 }
    };
}

// ─── Fakes ──────────────────────────────────────────────────────────────────

/// <summary>IToolUseRunner stub that returns a pre-configured result and records invocations.</summary>
internal sealed class FakeToolUseRunner(ToolLoopResult result) : IToolUseRunner
{
    public bool WasCalled { get; private set; }
    public string? LastRequiredFinalTool { get; private set; }

    public Task<ToolLoopResult> RunAsync(
        ILlmClient client,
        string model,
        string systemPrompt,
        string initialUserPrompt,
        IReadOnlyList<ToolDefinition> boundTools,
        string? requiredFinalTool,
        ToolLoopOptions options,
        ToolInvocationContext invocationContext,
        CancellationToken ct = default)
    {
        WasCalled = true;
        LastRequiredFinalTool = requiredFinalTool;
        return Task.FromResult(result);
    }
}

/// <summary>IToolDefinitionRepository stub that returns pre-seeded tool definitions by name.</summary>
internal sealed class FakeToolDefinitionRepository(params ToolDefinition[] tools) : IToolDefinitionRepository
{
    private readonly Dictionary<string, ToolDefinition> _map =
        tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

    public Task<ToolDefinition?> GetByNameAsync(string name, CancellationToken ct = default) =>
        Task.FromResult(_map.GetValueOrDefault(name));

    public Task<IReadOnlyList<ToolDefinition>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ToolDefinition>>(_map.Values.ToList());

    public Task<IReadOnlyList<ToolDefinition>> GetSystemToolsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ToolDefinition>>(_map.Values.Where(t => t.IsSystem).ToList());

    public Task<IReadOnlyList<ToolDefinition>> GetCustomToolsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ToolDefinition>>(_map.Values.Where(t => !t.IsSystem).ToList());

    public Task UpsertAsync(ToolDefinition tool, CancellationToken ct = default) => Task.CompletedTask;

    public Task DeleteAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>LLM client stub that replays a fixed sequence of responses and tracks call count.</summary>
internal sealed class ToolUseTestLlmClient(params LlmResponse[] responses) : ILlmClient
{
    public int CallCount { get; private set; }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var idx = Math.Min(CallCount, responses.Length - 1);
        CallCount++;
        return Task.FromResult(responses[idx]);
    }
}

/// <summary>ILlmClientResolver variant that always returns SupportsAgenticTools=false.</summary>
internal sealed class NonAgenticTestLlmClientResolver(
    ILlmClient client,
    string model = "test-model",
    int maxTokens = 4096) : ILlmClientResolver
{
    public (ILlmClient Client, string Model, int MaxTokens) ForActor(string actorName) =>
        (client, model, maxTokens);

    public (ILlmClient Client, string Model, int MaxTokens) ForProfile(string provider, string profileModel, int? profileMaxTokens) =>
        (client, profileModel, profileMaxTokens ?? maxTokens);

    public bool SupportsAgenticTools(string providerName) => false;
        public bool SupportsStructuredOutputs(string providerName) => false;
}
