using System.Text.Json;

namespace Geef.Atelier.Infrastructure.Llm;

public interface ILlmClient
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// A single turn in a multi-turn conversation (system / user / assistant / tool).
/// </summary>
public sealed record LlmMessage
{
    public required string Role { get; init; }                           // "system" | "user" | "assistant" | "tool"
    public string? Content { get; init; }                                // null for assistant messages that only contain tool_calls
    public IReadOnlyList<LlmToolCall>? ToolCalls { get; init; }         // set on assistant messages
    public string? ToolCallId { get; init; }                             // set on tool-result messages (role="tool")
    public string? Name { get; init; }                                   // tool name on role="tool" messages

    // Factory helpers
    public static LlmMessage System(string content) => new() { Role = "system", Content = content };
    public static LlmMessage User(string content) => new() { Role = "user", Content = content };
    public static LlmMessage AssistantText(string content) => new() { Role = "assistant", Content = content };
    public static LlmMessage AssistantToolCalls(IReadOnlyList<LlmToolCall> calls) => new() { Role = "assistant", ToolCalls = calls };
    public static LlmMessage ToolResult(string toolCallId, string toolName, string content) => new()
        { Role = "tool", Content = content, ToolCallId = toolCallId, Name = toolName };
}

/// <summary>A single tool call emitted by the LLM in an assistant message.</summary>
public sealed record LlmToolCall
{
    public required string Id { get; init; }              // tool_call_id from the API
    public required string Name { get; init; }            // function name
    public required string ArgumentsJson { get; init; }   // raw JSON string of arguments
}

public sealed record LlmRequest
{
    public required string Model { get; init; }
    public required string SystemPrompt { get; init; }
    public required string UserPrompt { get; init; }
    public int MaxTokens { get; init; } = 16384;
    public IReadOnlyList<LlmTool>? Tools { get; init; }

    /// <summary>
    /// Null = no tool_choice. "function:&lt;name&gt;" = force specific tool. "auto" = model decides.
    /// </summary>
    public string? ToolChoice { get; init; }

    /// <summary>
    /// Full message history for multi-turn agentic loops.
    /// When set, this takes precedence over <see cref="SystemPrompt"/> / <see cref="UserPrompt"/>
    /// (which are still required for non-loop callers).
    /// </summary>
    public IReadOnlyList<LlmMessage>? Messages { get; init; }

    /// <summary>
    /// When true, the CLI proxy writes the document to draft.md in an ephemeral workspace and
    /// runs the CLI agent in file-edit mode instead of text-completion mode. Only set for
    /// CLI-provider executor calls (claude-cli, codex-cli). API providers ignore these fields.
    /// </summary>
    public bool DocumentMode { get; init; }

    /// <summary>
    /// The current document content to place in draft.md before the CLI runs.
    /// Empty string on iteration 1 (no prior draft). Only meaningful when DocumentMode=true.
    /// </summary>
    public string? Document { get; init; }

    /// <summary>
    /// Large, static background context (web-research grounding + advisor consultations) kept
    /// separate from the steering instruction. In document mode the proxy materialises this as
    /// context.md in the workspace when it exceeds a size threshold (and references it from the
    /// prompt), otherwise it is prepended inline. Keeping it out of the argv prompt avoids the
    /// per-argument OS limit (MAX_ARG_STRLEN, 128 KB). Only meaningful when DocumentMode=true.
    /// </summary>
    public string? ContextDocument { get; init; }
}

public sealed record LlmResponse
{
    public required string Text { get; init; }

    /// <summary>Name of the tool called; null when finish_reason != "tool_calls".</summary>
    public string? ToolName { get; init; }

    /// <summary>Raw JSON string of the tool arguments; null when no tool was called.</summary>
    public string? ToolArgumentsJson { get; init; }

    /// <summary>All tool calls in the response (may be empty). Use instead of ToolName/ToolArgumentsJson for multi-turn loops.</summary>
    public IReadOnlyList<LlmToolCall> AllToolCalls { get; init; } = [];

    public required LlmTokenUsage TokenUsage { get; init; }
    public required string FinishReason { get; init; }
}

public sealed record LlmTokenUsage
{
    public required int InputTokens { get; init; }
    public required int OutputTokens { get; init; }
}

public sealed record LlmTool
{
    public required string Name { get; init; }
    public required string Description { get; init; }

    /// <summary>JSON Schema as a <see cref="JsonElement"/> — serialized verbatim into the tools array.</summary>
    public required JsonElement InputSchema { get; init; }
}
