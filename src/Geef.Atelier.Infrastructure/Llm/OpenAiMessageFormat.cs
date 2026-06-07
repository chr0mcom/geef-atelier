using System.Text.Json;
using System.Text.Json.Serialization;

namespace Geef.Atelier.Infrastructure.Llm;

internal static class OpenAiMessageFormat
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal static string SerializeRequest(LlmRequest request)
    {
        var messages = new[]
        {
            new MessageDto { Role = "system", Content = request.SystemPrompt },
            new MessageDto { Role = "user",   Content = request.UserPrompt }
        };

        ToolDto[]? tools = null;
        if (request.Tools is { Count: > 0 })
        {
            tools = request.Tools.Select(t => new ToolDto
            {
                Type = "function",
                Function = new FunctionDefDto
                {
                    Name = t.Name,
                    Description = t.Description,
                    Parameters = t.InputSchema
                }
            }).ToArray();
        }

        object? toolChoice = BuildToolChoice(request.ToolChoice);

        var body = new RequestBodyDto
        {
            Model        = request.Model,
            Messages     = messages,
            MaxTokens    = request.MaxTokens,
            Tools        = tools,
            ToolChoice   = toolChoice,
            // bool? null → omitted by WhenWritingNull so real OpenAI/Anthropic APIs never see these fields.
            DocumentMode    = request.DocumentMode ? true : null,
            Document        = request.DocumentMode ? request.Document : null,
            ContextDocument = request.DocumentMode ? request.ContextDocument : null,
        };

        return JsonSerializer.Serialize(body, WriteOptions);
    }

    internal static LlmResponse DeserializeResponse(string json)
    {
        var api = JsonSerializer.Deserialize<ApiResponseDto>(json, ReadOptions)
            ?? throw new JsonException("Unexpected null response from LLM API.");

        if (api.Choices is not { Length: > 0 })
            throw new JsonException("LLM API response contained no choices.");

        var choice  = api.Choices[0];
        var message = choice.Message
            ?? throw new JsonException("LLM API choice missing 'message'.");

        var text = message.Content ?? string.Empty;

        string? toolName           = null;
        string? toolArgumentsJson  = null;

        if (message.ToolCalls is { Length: > 0 })
        {
            var call = message.ToolCalls[0];
            toolName          = call.Function?.Name;
            toolArgumentsJson = call.Function?.Arguments;
        }

        var usage = api.Usage;

        return new LlmResponse
        {
            Text              = text,
            ToolName          = toolName,
            ToolArgumentsJson = toolArgumentsJson,
            FinishReason      = choice.FinishReason ?? string.Empty,
            TokenUsage        = new LlmTokenUsage
            {
                InputTokens  = usage?.PromptTokens     ?? 0,
                OutputTokens = usage?.CompletionTokens ?? 0
            }
        };
    }

    private static object? BuildToolChoice(string? toolChoice) => toolChoice switch
    {
        null                  => null,
        "auto"                => new { type = "auto" },
        var s when s.StartsWith("function:", StringComparison.Ordinal)
                              => new { type = "function", function = new { name = s["function:".Length..] } },
        _                     => null
    };

    // --- DTOs ---

    private sealed class RequestBodyDto
    {
        [JsonPropertyName("model")]         public string Model { get; set; } = "";
        [JsonPropertyName("messages")]      public MessageDto[] Messages { get; set; } = [];
        [JsonPropertyName("max_tokens")]    public int MaxTokens { get; set; }
        [JsonPropertyName("tools")]         public ToolDto[]? Tools { get; set; }
        [JsonPropertyName("tool_choice")]   public object? ToolChoice { get; set; }
        // Document-mode fields — null (omitted) for non-CLI / non-document-mode requests.
        [JsonPropertyName("document_mode")]    public bool? DocumentMode { get; set; }
        [JsonPropertyName("document")]         public string? Document { get; set; }
        [JsonPropertyName("context_document")] public string? ContextDocument { get; set; }
    }

    private sealed class MessageDto
    {
        [JsonPropertyName("role")]    public string Role { get; set; } = "";
        [JsonPropertyName("content")] public string? Content { get; set; }
    }

    private sealed class ToolDto
    {
        [JsonPropertyName("type")]     public string Type { get; set; } = "function";
        [JsonPropertyName("function")] public FunctionDefDto? Function { get; set; }
    }

    private sealed class FunctionDefDto
    {
        [JsonPropertyName("name")]        public string Name { get; set; } = "";
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("parameters")]  public JsonElement Parameters { get; set; }
    }

    private sealed class ApiResponseDto
    {
        [JsonPropertyName("choices")] public ChoiceDto[]? Choices { get; init; }
        [JsonPropertyName("usage")]   public UsageDto? Usage { get; init; }
    }

    private sealed class ChoiceDto
    {
        [JsonPropertyName("message")]       public MessageResponseDto? Message { get; init; }
        [JsonPropertyName("finish_reason")] public string? FinishReason { get; init; }
    }

    private sealed class MessageResponseDto
    {
        [JsonPropertyName("content")]    public string? Content { get; init; }
        [JsonPropertyName("tool_calls")] public ToolCallDto[]? ToolCalls { get; init; }
    }

    private sealed class ToolCallDto
    {
        [JsonPropertyName("function")] public FunctionCallDto? Function { get; init; }
    }

    private sealed class FunctionCallDto
    {
        [JsonPropertyName("name")]      public string? Name { get; init; }
        [JsonPropertyName("arguments")] public string? Arguments { get; init; }
    }

    private sealed class UsageDto
    {
        [JsonPropertyName("prompt_tokens")]     public int PromptTokens { get; init; }
        [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; init; }
    }
}
