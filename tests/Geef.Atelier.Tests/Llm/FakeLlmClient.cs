using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Tests.Llm;

/// <summary>
/// Deterministic <see cref="ILlmClient"/> for pipeline tests.
/// Detects executor calls (no tools) vs reviewer calls (with tools) by <see cref="LlmRequest.Tools"/>.
/// In iteration 1 reviewers reject; in iteration 2+ they approve.
/// </summary>
internal sealed class FakeLlmClient : ILlmClient
{
    private int _executorCallCount;

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
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

    public static LlmResponse MakeTextResponse(string text) => new()
    {
        Text              = text,
        ToolName          = null,
        ToolArgumentsJson = null,
        FinishReason      = "stop",
        TokenUsage        = new LlmTokenUsage { InputTokens = 10, OutputTokens = 20 }
    };

    public static LlmResponse MakeRejectResponse(string findingMessage) => new()
    {
        Text              = "",
        FinishReason      = "tool_calls",
        ToolName          = "submit_review",
        ToolArgumentsJson = $$$"""{"approved":false,"findings":[{"severity":"minor","message":"{{{findingMessage}}}"}]}""",
        TokenUsage        = new LlmTokenUsage { InputTokens = 8, OutputTokens = 15 }
    };

    public static LlmResponse MakeApproveResponse() => new()
    {
        Text              = "",
        FinishReason      = "tool_calls",
        ToolName          = "submit_review",
        ToolArgumentsJson = """{"approved":true,"findings":[]}""",
        TokenUsage        = new LlmTokenUsage { InputTokens = 8, OutputTokens = 5 }
    };

    public static LlmResponse MakeCriticalResponse(string findingMessage) => new()
    {
        Text              = "",
        FinishReason      = "tool_calls",
        ToolName          = "submit_review",
        ToolArgumentsJson = $$$"""{"approved":false,"findings":[{"severity":"critical","message":"{{{findingMessage}}}"}]}""",
        TokenUsage        = new LlmTokenUsage { InputTokens = 8, OutputTokens = 15 }
    };
}

/// <summary>
/// Always returns a Critical finding regardless of iteration — used for AbortOnCritical tests.
/// </summary>
internal sealed class CriticalFakeLlmClient : ILlmClient
{
    private int _executorCallCount;

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        if (request.Tools is null || request.Tools.Count == 0)
        {
            Interlocked.Increment(ref _executorCallCount);
            return Task.FromResult(FakeLlmClient.MakeTextResponse("Draft text."));
        }

        return Task.FromResult(FakeLlmClient.MakeCriticalResponse("Critical: fatal briefing violation."));
    }
}
