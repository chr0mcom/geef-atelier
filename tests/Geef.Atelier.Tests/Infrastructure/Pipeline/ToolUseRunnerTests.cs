using System.Text.Json;
using Geef.Atelier.Application.Tools;
using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Pipeline;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Infrastructure.Pipeline;

/// <summary>
/// Unit tests for <see cref="ToolUseRunner"/> covering the six key scenarios:
/// no-tool-call, one tool call, required-final-tool, cap, tool failure, and cancellation.
/// </summary>
public sealed class ToolUseRunnerTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static ToolDefinition MakeTool(string name = "search-tool") =>
        new(
            Name: name,
            DisplayName: "Search Tool",
            Description: "Searches for information.",
            ToolType: ToolType.StaticContext,
            Settings: new Dictionary<string, string>
            {
                [ToolDefinitionSettingsKeys.StaticContent] = "search result"
            },
            SecretRef: null,
            LlmSchema: JsonDocument.Parse("{}").RootElement,
            AccessClass: ToolAccessClass.ReadOnly,
            IsSystem: false);

    private static ToolInvocationContext MakeContext() =>
        new(
            RunId: Guid.NewGuid(),
            IterationNumber: 0,
            ActorType: "executor",
            ActorName: "default-executor",
            Sequence: 0);

    private static ToolLoopOptions DefaultOptions => new() { MaxToolCalls = 3, PerToolTimeout = TimeSpan.FromSeconds(5) };

    private static ToolUseRunner BuildRunner(IToolExecutor? executor = null) =>
        new(
            executor ?? new StubToolExecutor(),
            new StubToolSchemaProvider(),
            costAccumulator: null,
            logger: NullLogger<ToolUseRunner>.Instance);

    // -------------------------------------------------------------------------
    // 1. No tool calls → FinalText
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_NoToolCalls_ReturnsFinalText()
    {
        var client = new SequencedLlmClient(
            MakeTextResponse("Here is the answer."));

        var runner = BuildRunner();
        var result = await runner.RunAsync(
            client, "gpt-test",
            "system", "user prompt",
            [MakeTool()], requiredFinalTool: null,
            DefaultOptions, MakeContext());

        Assert.Equal(ToolLoopEndReason.FinalText, result.EndReason);
        Assert.Equal("Here is the answer.", result.FinalText);
        Assert.Equal(0, result.ToolCallCount);
    }

    // -------------------------------------------------------------------------
    // 2. One tool call → tool executed → LLM returns text
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_OneToolCall_ExecutesAndLoops()
    {
        // Turn 1: model calls "search-tool"; Turn 2: model responds with text.
        var client = new SequencedLlmClient(
            MakeToolCallResponse("search-tool", """{"query":"test"}"""),
            MakeTextResponse("Result based on search."));

        var executor = new RecordingToolExecutor("search result");
        var runner   = BuildRunner(executor);

        var result = await runner.RunAsync(
            client, "gpt-test",
            "system", "user prompt",
            [MakeTool("search-tool")], requiredFinalTool: null,
            DefaultOptions, MakeContext());

        Assert.Equal(ToolLoopEndReason.FinalText, result.EndReason);
        Assert.Equal("Result based on search.", result.FinalText);
        Assert.Equal(1, result.ToolCallCount);
        Assert.Single(executor.Calls);
        Assert.Equal("search-tool", executor.Calls[0].ToolName);
    }

    // -------------------------------------------------------------------------
    // 3. Required final tool called → RequiredToolCalled
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_RequiredFinalTool_EndsOnCall()
    {
        const string submitArgs = """{"verdict":"approved","comment":"LGTM"}""";

        var client = new SequencedLlmClient(
            MakeToolCallResponse("submit-review", submitArgs));

        var runner = BuildRunner();
        var result = await runner.RunAsync(
            client, "gpt-test",
            "system", "user prompt",
            [MakeTool("other-tool")], requiredFinalTool: "submit-review",
            DefaultOptions, MakeContext());

        Assert.Equal(ToolLoopEndReason.RequiredToolCalled, result.EndReason);
        Assert.Equal(submitArgs, result.FinalText);
        // The required-final-tool call is not counted as an executed tool call
        // (the loop ends before executing it).
        Assert.Equal(0, result.ToolCallCount);
    }

    // -------------------------------------------------------------------------
    // 4. Cap reached → CapReached
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_CapReached_EndsWithCapReason()
    {
        // Model keeps calling the tool on every turn.
        var toolResponse = MakeToolCallResponse("search-tool", """{"query":"x"}""");
        var client = new SequencedLlmClient(
            toolResponse, toolResponse, toolResponse,
            toolResponse, toolResponse); // more than cap=3

        var runner = BuildRunner(new RecordingToolExecutor("ok"));
        var options = new ToolLoopOptions { MaxToolCalls = 3, PerToolTimeout = TimeSpan.FromSeconds(5) };

        var result = await runner.RunAsync(
            client, "gpt-test",
            "system", "user prompt",
            [MakeTool("search-tool")], requiredFinalTool: null,
            options, MakeContext());

        Assert.Equal(ToolLoopEndReason.CapReached, result.EndReason);
        Assert.Equal(3, result.ToolCallCount);
    }

    // -------------------------------------------------------------------------
    // 5. Tool throws → error appended as tool result, loop continues
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_ToolFailure_ContinuesLoop()
    {
        var client = new SequencedLlmClient(
            MakeToolCallResponse("search-tool", """{"query":"x"}"""),
            MakeTextResponse("Fallback answer."));

        var failingExecutor = new ThrowingToolExecutor("Simulated failure");
        var runner          = BuildRunner(failingExecutor);

        var result = await runner.RunAsync(
            client, "gpt-test",
            "system", "user prompt",
            [MakeTool("search-tool")], requiredFinalTool: null,
            DefaultOptions, MakeContext());

        // Loop must not abort — it continues and returns the final text response.
        Assert.Equal(ToolLoopEndReason.FinalText, result.EndReason);
        Assert.Equal("Fallback answer.", result.FinalText);
        Assert.Equal(1, result.ToolCallCount);

        // The error must have been forwarded to the LLM as a tool-result message.
        var secondRequest = client.Requests[1];
        Assert.NotNull(secondRequest.Messages);
        var toolResultMsg = secondRequest.Messages!
            .FirstOrDefault(m => m.Role == "tool");
        Assert.NotNull(toolResultMsg);
        Assert.Contains("Simulated failure", toolResultMsg!.Content);
    }

    // -------------------------------------------------------------------------
    // 6. Cancellation → OperationCanceledException propagates
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_Cancellation_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // already cancelled before the call

        var client = new SequencedLlmClient(MakeTextResponse("should not reach"));
        var runner = BuildRunner();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            runner.RunAsync(
                client, "gpt-test",
                "system", "user prompt",
                [MakeTool()], requiredFinalTool: null,
                DefaultOptions, MakeContext(),
                cts.Token));
    }

    // -------------------------------------------------------------------------
    // 7. Token usage accumulated across turns
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_TokenUsage_AccumulatesAcrossTurns()
    {
        var client = new SequencedLlmClient(
            MakeToolCallResponse("search-tool", """{"query":"x"}"""),  // 10+5 tokens
            MakeTextResponse("done"));                                   // 20+8 tokens

        var runner = BuildRunner(new RecordingToolExecutor("ok"));

        var result = await runner.RunAsync(
            client, "gpt-test",
            "system", "user",
            [MakeTool("search-tool")], requiredFinalTool: null,
            DefaultOptions, MakeContext());

        Assert.Equal(30, result.TotalTokenUsage.InputTokens);
        Assert.Equal(13, result.TotalTokenUsage.OutputTokens);
    }

    // -------------------------------------------------------------------------
    // Response factories
    // -------------------------------------------------------------------------

    private static LlmResponse MakeTextResponse(string text) => new()
    {
        Text         = text,
        FinishReason = "stop",
        AllToolCalls = [],
        TokenUsage   = new LlmTokenUsage { InputTokens = 20, OutputTokens = 8 }
    };

    private static LlmResponse MakeToolCallResponse(string toolName, string argsJson) => new()
    {
        Text         = "",
        FinishReason = "tool_calls",
        AllToolCalls =
        [
            new LlmToolCall
            {
                Id            = Guid.NewGuid().ToString("N"),
                Name          = toolName,
                ArgumentsJson = argsJson
            }
        ],
        TokenUsage = new LlmTokenUsage { InputTokens = 10, OutputTokens = 5 }
    };
}

// =============================================================================
// Test doubles
// =============================================================================

/// <summary>LLM client that returns a pre-configured sequence of responses.</summary>
internal sealed class SequencedLlmClient : ILlmClient
{
    private readonly Queue<LlmResponse> _responses;
    private readonly List<LlmRequest>  _requests = [];

    public SequencedLlmClient(params LlmResponse[] responses)
        => _responses = new Queue<LlmResponse>(responses);

    public IReadOnlyList<LlmRequest> Requests => _requests;

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _requests.Add(request);
        if (_responses.Count == 0)
            throw new InvalidOperationException("No more fake responses configured.");
        return Task.FromResult(_responses.Dequeue());
    }
}

/// <summary>Tool executor that records calls and returns a fixed output string.</summary>
internal sealed class RecordingToolExecutor(string output) : IToolExecutor
{
    private readonly List<(string ToolName, string InputJson)> _calls = [];

    public IReadOnlyList<(string ToolName, string InputJson)> Calls => _calls;

    public Task<ToolExecutionResult> ExecuteAsync(
        ToolDefinition tool,
        string inputJson,
        ToolInvocationContext ctx,
        CancellationToken ct = default)
    {
        _calls.Add((tool.Name, inputJson));
        return Task.FromResult(new ToolExecutionResult(output, null, null));
    }
}

/// <summary>Tool executor that always throws an exception — simulates a broken tool.</summary>
internal sealed class ThrowingToolExecutor(string message) : IToolExecutor
{
    public Task<ToolExecutionResult> ExecuteAsync(
        ToolDefinition tool,
        string inputJson,
        ToolInvocationContext ctx,
        CancellationToken ct = default)
        => throw new InvalidOperationException(message);
}

/// <summary>Stub tool executor (does nothing, returns empty success).</summary>
internal sealed class StubToolExecutor : IToolExecutor
{
    public Task<ToolExecutionResult> ExecuteAsync(
        ToolDefinition tool,
        string inputJson,
        ToolInvocationContext ctx,
        CancellationToken ct = default)
        => Task.FromResult(new ToolExecutionResult("", null, null));
}

/// <summary>Minimal <see cref="IToolSchemaProvider"/> that produces a static query schema.</summary>
internal sealed class StubToolSchemaProvider : IToolSchemaProvider
{
    private const string QuerySchema =
        """{"type":"object","properties":{"query":{"type":"string"}},"required":["query"]}""";

    public Geef.Atelier.Application.Tools.ToolSchema GetSchema(ToolDefinition tool)
        => new(tool.Name, tool.Description, QuerySchema);
}
