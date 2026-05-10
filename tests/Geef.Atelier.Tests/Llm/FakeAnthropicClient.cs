using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Tests.Llm;

/// <summary>
/// Deterministic <see cref="IAnthropicClient"/> for pipeline tests.
/// Detects executor calls (no tools) vs reviewer calls (with tools) by <see cref="AnthropicRequest.Tools"/>.
/// In iteration 1 reviewers reject; in iteration 2+ they approve.
/// </summary>
internal sealed class FakeAnthropicClient : IAnthropicClient
{
    private int _executorCallCount;

    public Task<AnthropicResponse> CompleteAsync(AnthropicRequest request, CancellationToken ct)
    {
        if (request.Tools is null || request.Tools.Count == 0)
        {
            var iter = Interlocked.Increment(ref _executorCallCount);
            return Task.FromResult(MakeTextResponse($"Revised draft (iteration {iter})."));
        }

        // Reviewer call — reject only while the executor has been called exactly once (iteration 1).
        bool isFirstIteration = Volatile.Read(ref _executorCallCount) <= 1;
        return isFirstIteration
            ? Task.FromResult(MakeRejectResponse("Fake finding: needs improvement."))
            : Task.FromResult(MakeApproveResponse());
    }

    public static AnthropicResponse MakeTextResponse(string text) => new()
    {
        Text         = text,
        ToolInputJson = null,
        StopReason   = "end_turn",
        TokenUsage   = new AnthropicTokenUsage { InputTokens = 10, OutputTokens = 20 }
    };

    public static AnthropicResponse MakeRejectResponse(string findingMessage) => new()
    {
        Text         = "",
        StopReason   = "tool_use",
        ToolInputJson = $$$"""{"approved":false,"findings":[{"severity":"warning","message":"{{{findingMessage}}}"}]}""",
        TokenUsage   = new AnthropicTokenUsage { InputTokens = 8, OutputTokens = 15 }
    };

    public static AnthropicResponse MakeApproveResponse() => new()
    {
        Text         = "",
        StopReason   = "tool_use",
        ToolInputJson = """{"approved":true,"findings":[]}""",
        TokenUsage   = new AnthropicTokenUsage { InputTokens = 8, OutputTokens = 5 }
    };

    public static AnthropicResponse MakeCriticalResponse(string findingMessage) => new()
    {
        Text         = "",
        StopReason   = "tool_use",
        ToolInputJson = $$$"""{"approved":false,"findings":[{"severity":"critical","message":"{{{findingMessage}}}"}]}""",
        TokenUsage   = new AnthropicTokenUsage { InputTokens = 8, OutputTokens = 15 }
    };
}

/// <summary>
/// Always returns a Critical finding regardless of iteration — used for AbortOnCritical tests.
/// </summary>
internal sealed class CriticalFakeAnthropicClient : IAnthropicClient
{
    private int _executorCallCount;

    public Task<AnthropicResponse> CompleteAsync(AnthropicRequest request, CancellationToken ct)
    {
        if (request.Tools is null || request.Tools.Count == 0)
        {
            Interlocked.Increment(ref _executorCallCount);
            return Task.FromResult(FakeAnthropicClient.MakeTextResponse("Draft text."));
        }

        return Task.FromResult(FakeAnthropicClient.MakeCriticalResponse("Critical: fatal briefing violation."));
    }
}
