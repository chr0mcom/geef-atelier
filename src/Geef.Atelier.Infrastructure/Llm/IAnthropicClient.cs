using System.Text.Json;

namespace Geef.Atelier.Infrastructure.Llm;

public interface IAnthropicClient
{
    Task<AnthropicResponse> CompleteAsync(AnthropicRequest request, CancellationToken cancellationToken);
}

public sealed record AnthropicRequest
{
    public required string Model { get; init; }
    public required string SystemPrompt { get; init; }
    public required string UserPrompt { get; init; }
    public int MaxTokens { get; init; } = 4096;
    public IReadOnlyList<AnthropicTool>? Tools { get; init; }

    /// <summary>
    /// Null = no tool_choice. "tool:&lt;name&gt;" = force specific tool. "auto"/"any" = pass-through.
    /// </summary>
    public string? ToolChoice { get; init; }
}

public sealed record AnthropicResponse
{
    public required string Text { get; init; }

    /// <summary>
    /// Raw JSON string of the tool input block; null when stop_reason != "tool_use".
    /// </summary>
    public string? ToolInputJson { get; init; }

    public required AnthropicTokenUsage TokenUsage { get; init; }
    public required string StopReason { get; init; }
}

public sealed record AnthropicTokenUsage
{
    public required int InputTokens { get; init; }
    public required int OutputTokens { get; init; }
}

public sealed record AnthropicTool
{
    public required string Name { get; init; }
    public required string Description { get; init; }

    /// <summary>
    /// JSON Schema as a <see cref="JsonElement"/> — serialized verbatim into the tools array.
    /// </summary>
    public required JsonElement InputSchema { get; init; }
}
