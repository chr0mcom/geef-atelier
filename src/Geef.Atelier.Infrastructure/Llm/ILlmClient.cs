using System.Text.Json;

namespace Geef.Atelier.Infrastructure.Llm;

public interface ILlmClient
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken);
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
}

public sealed record LlmResponse
{
    public required string Text { get; init; }

    /// <summary>Name of the tool called; null when finish_reason != "tool_calls".</summary>
    public string? ToolName { get; init; }

    /// <summary>Raw JSON string of the tool arguments; null when no tool was called.</summary>
    public string? ToolArgumentsJson { get; init; }

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
