using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Geef.Atelier.Infrastructure.Llm;

internal static class AnthropicMessageFormat
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal static string SerializeRequest(AnthropicRequest request)
    {
        var body = new RequestBody
        {
            Model = request.Model,
            MaxTokens = request.MaxTokens,
            System = request.SystemPrompt,
            Messages = [new Message { Role = "user", Content = request.UserPrompt }],
            Tools = request.Tools?.Select(t => new ToolBody
            {
                Name = t.Name,
                Description = t.Description,
                InputSchema = t.InputSchema
            }).ToArray()
        };

        if (request.ToolChoice is not null)
            body.ToolChoice = BuildToolChoice(request.ToolChoice);

        return JsonSerializer.Serialize(body, WriteOptions);
    }

    internal static AnthropicResponse DeserializeResponse(string json)
    {
        var api = JsonSerializer.Deserialize<ApiResponse>(json, ReadOptions)
            ?? throw new JsonException("Unexpected null response from Anthropic API.");

        var textParts = api.Content
            .Where(b => b.Type == "text" && b.Text is not null)
            .Select(b => b.Text!);
        var text = string.Join("\n", textParts);

        var toolBlock = api.Content.FirstOrDefault(b => b.Type == "tool_use");
        string? toolInputJson = toolBlock?.Input?.GetRawText();

        return new AnthropicResponse
        {
            Text = text,
            ToolInputJson = toolInputJson,
            StopReason = api.StopReason ?? "",
            TokenUsage = new AnthropicTokenUsage
            {
                InputTokens = api.Usage?.InputTokens ?? 0,
                OutputTokens = api.Usage?.OutputTokens ?? 0
            }
        };
    }

    private static object BuildToolChoice(string toolChoice) => toolChoice switch
    {
        "auto" => new { type = "auto" },
        "any"  => new { type = "any" },
        _ when toolChoice.StartsWith("tool:", StringComparison.Ordinal)
               => new { type = "tool", name = toolChoice["tool:".Length..] },
        _      => new { type = "auto" }
    };

    // --- Internal DTOs for serialization ---

    private sealed class RequestBody
    {
        [JsonPropertyName("model")]      public string Model { get; set; } = "";
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
        [JsonPropertyName("system")]     public string System { get; set; } = "";
        [JsonPropertyName("messages")]   public Message[] Messages { get; set; } = [];
        [JsonPropertyName("tools")]      public ToolBody[]? Tools { get; set; }
        [JsonPropertyName("tool_choice")] public object? ToolChoice { get; set; }
    }

    private sealed class Message
    {
        [JsonPropertyName("role")]    public string Role { get; set; } = "";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
    }

    private sealed class ToolBody
    {
        [JsonPropertyName("name")]         public string Name { get; set; } = "";
        [JsonPropertyName("description")]  public string Description { get; set; } = "";
        [JsonPropertyName("input_schema")] public JsonElement InputSchema { get; set; }
    }

    private sealed class ApiResponse
    {
        [JsonPropertyName("content")]     public List<ContentBlock> Content { get; init; } = [];
        [JsonPropertyName("stop_reason")] public string? StopReason { get; init; }
        [JsonPropertyName("usage")]       public ApiUsage? Usage { get; init; }
    }

    private sealed class ContentBlock
    {
        [JsonPropertyName("type")]  public string Type { get; init; } = "";
        [JsonPropertyName("text")]  public string? Text { get; init; }
        [JsonPropertyName("input")] public JsonElement? Input { get; init; }
    }

    private sealed class ApiUsage
    {
        [JsonPropertyName("input_tokens")]  public int InputTokens { get; init; }
        [JsonPropertyName("output_tokens")] public int OutputTokens { get; init; }
    }
}
