using Geef.Atelier.Application.Tools;
using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Infrastructure.Pipeline;

/// <summary>Options governing a single agentic tool-use loop.</summary>
public sealed record ToolLoopOptions
{
    /// <summary>Maximum number of tool calls allowed before the loop ends with <see cref="ToolLoopEndReason.CapReached"/>.</summary>
    public int MaxToolCalls { get; init; } = 5;

    /// <summary>Per-tool execution timeout. The loop cancels a single tool call after this duration.</summary>
    public TimeSpan PerToolTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>Result of a completed tool-use loop.</summary>
public sealed record ToolLoopResult
{
    /// <summary>
    /// Final text response from the LLM (after all tool rounds).
    /// When <see cref="EndReason"/> is <see cref="ToolLoopEndReason.RequiredToolCalled"/> this
    /// contains the <c>ArgumentsJson</c> of the required tool call so the caller can parse it directly.
    /// </summary>
    public required string FinalText { get; init; }

    /// <summary>How the loop ended.</summary>
    public required ToolLoopEndReason EndReason { get; init; }

    /// <summary>Number of tool calls made during the loop.</summary>
    public int ToolCallCount { get; init; }

    /// <summary>Total LLM token usage across all turns in the loop.</summary>
    public LlmTokenUsage TotalTokenUsage { get; init; } = new() { InputTokens = 0, OutputTokens = 0 };
}

/// <summary>Describes how an agentic tool-use loop concluded.</summary>
public enum ToolLoopEndReason
{
    /// <summary>The model returned a text response without calling any tool.</summary>
    FinalText = 0,

    /// <summary>The model called the designated final tool (e.g. <c>submit_review</c>).</summary>
    RequiredToolCalled = 1,

    /// <summary>The per-turn cap (<see cref="ToolLoopOptions.MaxToolCalls"/>) was reached.</summary>
    CapReached = 2,

    /// <summary>The outer <see cref="CancellationToken"/> was cancelled.</summary>
    Cancelled = 3
}

/// <summary>Drives an agentic tool-use loop: model calls tools, gets results, loops until done.</summary>
public interface IToolUseRunner
{
    /// <summary>
    /// Runs the tool-use loop with the given bound tools.
    /// </summary>
    /// <param name="client">LLM client to use for each turn.</param>
    /// <param name="model">Model identifier.</param>
    /// <param name="systemPrompt">System prompt (prepended as system message).</param>
    /// <param name="initialUserPrompt">Initial user turn.</param>
    /// <param name="boundTools">Tools the model may call.</param>
    /// <param name="requiredFinalTool">
    /// When non-null the loop ends as soon as the model calls this tool.
    /// <see cref="ToolLoopResult.FinalText"/> will contain the tool's <c>ArgumentsJson</c>.
    /// </param>
    /// <param name="options">Loop options (cap + per-tool timeout).</param>
    /// <param name="invocationContext">Audit context — passed to <see cref="IToolExecutor"/>.</param>
    /// <param name="ct">Outer cancellation token.</param>
    Task<ToolLoopResult> RunAsync(
        ILlmClient client,
        string model,
        string systemPrompt,
        string initialUserPrompt,
        IReadOnlyList<ToolDefinition> boundTools,
        string? requiredFinalTool,
        ToolLoopOptions options,
        ToolInvocationContext invocationContext,
        CancellationToken ct = default);
}
