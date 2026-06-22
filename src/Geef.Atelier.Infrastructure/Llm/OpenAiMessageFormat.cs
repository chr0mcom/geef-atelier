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
        MessageDto[] messages;

        if (request.Messages is { Count: > 0 })
        {
            // Multi-turn path: serialize full message history.
            messages = request.Messages.Select(m => new MessageDto
            {
                Role       = m.Role,
                Content    = m.Content,
                ToolCalls  = m.ToolCalls is { Count: > 0 }
                    ? m.ToolCalls.Select(tc => new OutboundToolCallDto
                    {
                        Id       = tc.Id,
                        Type     = "function",
                        Function = new FunctionCallOutDto
                        {
                            Name      = tc.Name,
                            Arguments = tc.ArgumentsJson
                        }
                    }).ToArray()
                    : null,
                ToolCallId = m.ToolCallId,
                Name       = m.Name,
            }).ToArray();
        }
        else
        {
            // Single-turn path: legacy system + user messages.
            messages = new[]
            {
                new MessageDto { Role = "system", Content = request.SystemPrompt },
                new MessageDto { Role = "user",   Content = request.UserPrompt }
            };
        }

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
        object? responseFormat = BuildResponseFormat(request.ResponseFormat);

        var body = new RequestBodyDto
        {
            Model          = request.Model,
            Messages       = messages,
            MaxTokens      = request.MaxTokens,
            Tools          = tools,
            ToolChoice     = toolChoice,
            ResponseFormat = responseFormat,
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
        LlmToolCall[] allToolCalls = [];

        if (message.ToolCalls is { Length: > 0 })
        {
            // Deserialize all tool calls for multi-turn loop consumers.
            allToolCalls = message.ToolCalls
                .Select(tc => new LlmToolCall
                {
                    Id            = tc.Id            ?? string.Empty,
                    Name          = tc.Function?.Name ?? string.Empty,
                    ArgumentsJson = tc.Function?.Arguments ?? string.Empty
                })
                .ToArray();

            // Keep legacy single-call properties pointing at index 0 for backwards compatibility.
            toolName          = allToolCalls[0].Name;
            toolArgumentsJson = allToolCalls[0].ArgumentsJson;
        }

        var usage = api.Usage;

        return new LlmResponse
        {
            Text              = text,
            ToolName          = toolName,
            ToolArgumentsJson = toolArgumentsJson,
            AllToolCalls      = allToolCalls,
            FinishReason      = choice.FinishReason ?? string.Empty,
            TokenUsage        = new LlmTokenUsage
            {
                InputTokens       = usage?.PromptTokens     ?? 0,
                OutputTokens      = usage?.CompletionTokens ?? 0,
                CachedInputTokens = usage?.PromptTokensDetails?.CachedTokens,
                ReasoningTokens   = usage?.CompletionTokensDetails?.ReasoningTokens
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

    private static object? BuildResponseFormat(LlmResponseFormat? rf)
    {
        if (rf is null) return null;
        if (rf.Type == "json_object") return new { type = "json_object" };
        if (rf.Type == "json_schema" && rf.Schema is { } schema)
        {
            return new
            {
                type = "json_schema",
                json_schema = new
                {
                    name   = rf.SchemaName ?? "response",
                    schema,
                    strict = rf.Strict
                }
            };
        }
        return null;
    }

    // --- DTOs ---

    private sealed class RequestBodyDto
    {
        [JsonPropertyName("model")]         public string Model { get; set; } = "";
        [JsonPropertyName("messages")]      public MessageDto[] Messages { get; set; } = [];
        [JsonPropertyName("max_tokens")]    public int MaxTokens { get; set; }
        [JsonPropertyName("tools")]         public ToolDto[]? Tools { get; set; }
        [JsonPropertyName("tool_choice")]   public object? ToolChoice { get; set; }
        [JsonPropertyName("response_format")] public object? ResponseFormat { get; set; }
        // Document-mode fields — null (omitted) for non-CLI / non-document-mode requests.
        [JsonPropertyName("document_mode")]    public bool? DocumentMode { get; set; }
        [JsonPropertyName("document")]         public string? Document { get; set; }
        [JsonPropertyName("context_document")] public string? ContextDocument { get; set; }
    }

    private sealed class MessageDto
    {
        [JsonPropertyName("role")]         public string Role { get; set; } = "";
        [JsonPropertyName("content")]      public string? Content { get; set; }
        [JsonPropertyName("tool_calls")]   public OutboundToolCallDto[]? ToolCalls { get; set; }
        [JsonPropertyName("tool_call_id")] public string? ToolCallId { get; set; }
        [JsonPropertyName("name")]         public string? Name { get; set; }
    }

    /// <summary>Outbound tool_call entry inside an assistant message (request serialization).</summary>
    private sealed class OutboundToolCallDto
    {
        [JsonPropertyName("id")]       public string Id { get; set; } = "";
        [JsonPropertyName("type")]     public string Type { get; set; } = "function";
        [JsonPropertyName("function")] public FunctionCallOutDto? Function { get; set; }
    }

    private sealed class FunctionCallOutDto
    {
        [JsonPropertyName("name")]      public string Name { get; set; } = "";
        [JsonPropertyName("arguments")] public string Arguments { get; set; } = "";
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
        [JsonPropertyName("id")]       public string? Id { get; init; }
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
        [JsonPropertyName("prompt_tokens_details")]     public TokenDetailsDto? PromptTokensDetails { get; init; }
        [JsonPropertyName("completion_tokens_details")] public TokenDetailsDto? CompletionTokensDetails { get; init; }
    }

    private sealed class TokenDetailsDto
    {
        [JsonPropertyName("cached_tokens")]    public int? CachedTokens { get; init; }
        [JsonPropertyName("reasoning_tokens")] public int? ReasoningTokens { get; init; }
    }
}
