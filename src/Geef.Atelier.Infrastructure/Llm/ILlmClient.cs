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
    public int MaxTokens { get; init; } = 4096;
    public IReadOnlyList<LlmTool>? Tools { get; init; }

    /// <summary>
    /// Null = no tool_choice. "function:&lt;name&gt;" = force specific tool. "auto" = model decides.
    /// </summary>
    public string? ToolChoice { get; init; }
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
