using System.Text.Json;
using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Tests.Infrastructure.Llm;

/// <summary>
/// Unit tests for multi-turn message serialization/deserialization in <see cref="OpenAiMessageFormat"/>
/// and for the <see cref="LlmMessage"/> factory helpers.
/// All tests use direct JSON manipulation — no HTTP calls.
/// </summary>
public sealed class MultiTurnMessageFormatTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static LlmRequest MinimalRequest(IReadOnlyList<LlmMessage>? messages = null) => new()
    {
        Model        = "test-model",
        SystemPrompt = "sys",
        UserPrompt   = "user",
        Messages     = messages
    };

    private static string FakeApiResponse(string content = "", ToolCallEntry[]? toolCalls = null)
    {
        var toolCallsJson = toolCalls is { Length: > 0 }
            ? ",\"tool_calls\":[" + string.Join(",", toolCalls.Select(tc =>
                $"{{\"id\":\"{tc.Id}\",\"type\":\"function\",\"function\":{{\"name\":\"{tc.Name}\",\"arguments\":\"{tc.ArgumentsEscaped}\"}}}}")) + "]"
            : "";

        var contentJson = content.Length > 0 ? $"\"content\":\"{content}\"" : "\"content\":null";

        return $$"""
            {
              "choices": [{
                "message": { "role": "assistant", {{contentJson}}{{toolCallsJson}} },
                "finish_reason": "{{(toolCalls is { Length: > 0 } ? "tool_calls" : "stop")}}"
              }],
              "usage": { "prompt_tokens": 10, "completion_tokens": 5 }
            }
            """;
    }

    private record ToolCallEntry(string Id, string Name, string ArgumentsEscaped);

    // ── SerializeRequest — WithMessages ─────────────────────────────────────

    [Fact]
    public void SerializeRequest_WithMessages_UsesMessagesArray()
    {
        var messages = new[]
        {
            LlmMessage.System("You are helpful."),
            LlmMessage.User("Hello world"),
            LlmMessage.AssistantText("Hi there!")
        };

        var json = OpenAiMessageFormat.SerializeRequest(MinimalRequest(messages));
        using var doc = JsonDocument.Parse(json);

        var arr = doc.RootElement.GetProperty("messages");
        Assert.Equal(3, arr.GetArrayLength());

        Assert.Equal("system",           arr[0].GetProperty("role").GetString());
        Assert.Equal("You are helpful.", arr[0].GetProperty("content").GetString());

        Assert.Equal("user",        arr[1].GetProperty("role").GetString());
        Assert.Equal("Hello world", arr[1].GetProperty("content").GetString());

        Assert.Equal("assistant", arr[2].GetProperty("role").GetString());
        Assert.Equal("Hi there!", arr[2].GetProperty("content").GetString());
    }

    [Fact]
    public void SerializeRequest_WithMessages_DoesNotFallBackToSystemUser()
    {
        var messages = new[] { LlmMessage.User("only user") };
        var json = OpenAiMessageFormat.SerializeRequest(MinimalRequest(messages));
        using var doc = JsonDocument.Parse(json);

        var arr = doc.RootElement.GetProperty("messages");
        Assert.Equal(1, arr.GetArrayLength());
        Assert.Equal("user", arr[0].GetProperty("role").GetString());
        // Ensure the default "sys" system prompt is NOT injected.
        Assert.DoesNotContain(arr.EnumerateArray(), m =>
            m.GetProperty("role").GetString() == "system");
    }

    // ── SerializeRequest — WithoutMessages (single-turn legacy) ─────────────

    [Fact]
    public void SerializeRequest_WithoutMessages_UsesSingleTurnFormat()
    {
        var json = OpenAiMessageFormat.SerializeRequest(MinimalRequest());
        using var doc = JsonDocument.Parse(json);

        var arr = doc.RootElement.GetProperty("messages");
        Assert.Equal(2, arr.GetArrayLength());
        Assert.Equal("system", arr[0].GetProperty("role").GetString());
        Assert.Equal("sys",    arr[0].GetProperty("content").GetString());
        Assert.Equal("user",   arr[1].GetProperty("role").GetString());
        Assert.Equal("user",   arr[1].GetProperty("content").GetString());
    }

    // ── SerializeRequest — AssistantWithToolCalls ────────────────────────────

    [Fact]
    public void SerializeRequest_AssistantWithToolCalls_SerializesCorrectly()
    {
        var toolCalls = new[]
        {
            new LlmToolCall { Id = "call_1", Name = "search", ArgumentsJson = "{\"q\":\"dotnet\"}" }
        };
        var messages = new[]
        {
            LlmMessage.User("find something"),
            LlmMessage.AssistantToolCalls(toolCalls)
        };

        var json = OpenAiMessageFormat.SerializeRequest(MinimalRequest(messages));
        using var doc = JsonDocument.Parse(json);

        var arr = doc.RootElement.GetProperty("messages");
        var assistant = arr[1];

        Assert.Equal("assistant", assistant.GetProperty("role").GetString());
        // content must be absent or null (WhenWritingNull omits it)
        if (assistant.TryGetProperty("content", out var content))
            Assert.Equal(JsonValueKind.Null, content.ValueKind);

        var tcs = assistant.GetProperty("tool_calls");
        Assert.Equal(1, tcs.GetArrayLength());
        Assert.Equal("call_1",            tcs[0].GetProperty("id").GetString());
        Assert.Equal("function",          tcs[0].GetProperty("type").GetString());
        Assert.Equal("search",            tcs[0].GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("{\"q\":\"dotnet\"}", tcs[0].GetProperty("function").GetProperty("arguments").GetString());
    }

    // ── SerializeRequest — ToolResultMessage ─────────────────────────────────

    [Fact]
    public void SerializeRequest_ToolResultMessage_SerializesCorrectly()
    {
        var messages = new[]
        {
            LlmMessage.ToolResult("call_1", "search", "result text")
        };

        var json = OpenAiMessageFormat.SerializeRequest(MinimalRequest(messages));
        using var doc = JsonDocument.Parse(json);

        var arr = doc.RootElement.GetProperty("messages");
        var toolMsg = arr[0];

        Assert.Equal("tool",        toolMsg.GetProperty("role").GetString());
        Assert.Equal("result text", toolMsg.GetProperty("content").GetString());
        Assert.Equal("call_1",      toolMsg.GetProperty("tool_call_id").GetString());
        Assert.Equal("search",      toolMsg.GetProperty("name").GetString());
    }

    // ── DeserializeResponse — MultipleToolCalls ──────────────────────────────

    [Fact]
    public void DeserializeResponse_MultipleToolCalls_AllInAllToolCalls()
    {
        var entries = new[]
        {
            new ToolCallEntry("c1", "tool_a", "{\\\"x\\\":1}"),
            new ToolCallEntry("c2", "tool_b", "{\\\"y\\\":2}"),
            new ToolCallEntry("c3", "tool_c", "{\\\"z\\\":3}")
        };

        var response = OpenAiMessageFormat.DeserializeResponse(FakeApiResponse(toolCalls: entries));

        Assert.Equal(3, response.AllToolCalls.Count);
        Assert.Equal("c1",     response.AllToolCalls[0].Id);
        Assert.Equal("tool_a", response.AllToolCalls[0].Name);
        Assert.Equal("c2",     response.AllToolCalls[1].Id);
        Assert.Equal("tool_b", response.AllToolCalls[1].Name);
        Assert.Equal("c3",     response.AllToolCalls[2].Id);
        Assert.Equal("tool_c", response.AllToolCalls[2].Name);
    }

    // ── DeserializeResponse — BackwardsCompatible (single tool call) ─────────

    [Fact]
    public void DeserializeResponse_SingleToolCall_BackwardsCompatible()
    {
        var entries = new[]
        {
            new ToolCallEntry("call_xyz", "submit_review", "{\\\"approved\\\":true}")
        };

        var response = OpenAiMessageFormat.DeserializeResponse(FakeApiResponse(toolCalls: entries));

        Assert.Equal("submit_review",      response.ToolName);
        Assert.Equal("{\"approved\":true}", response.ToolArgumentsJson);
        Assert.Single(response.AllToolCalls);
        Assert.Equal("call_xyz", response.AllToolCalls[0].Id);
    }

    // ── DeserializeResponse — NoToolCalls ────────────────────────────────────

    [Fact]
    public void DeserializeResponse_NoToolCalls_EmptyAllToolCalls()
    {
        var response = OpenAiMessageFormat.DeserializeResponse(FakeApiResponse("Hello!"));

        Assert.Empty(response.AllToolCalls);
        Assert.Null(response.ToolName);
        Assert.Null(response.ToolArgumentsJson);
        Assert.Equal("Hello!", response.Text);
    }

    // ── LlmMessage factory helpers ────────────────────────────────────────────

    [Fact]
    public void LlmMessage_FactoryHelpers_SetCorrectRoles()
    {
        var sys = LlmMessage.System("s");
        var usr = LlmMessage.User("u");
        var ast = LlmMessage.AssistantText("a");
        var res = LlmMessage.ToolResult("id", "my_tool", "result");

        Assert.Equal("system",    sys.Role);
        Assert.Equal("s",         sys.Content);

        Assert.Equal("user",      usr.Role);
        Assert.Equal("u",         usr.Content);

        Assert.Equal("assistant", ast.Role);
        Assert.Equal("a",         ast.Content);

        Assert.Equal("tool",      res.Role);
        Assert.Equal("result",    res.Content);
        Assert.Equal("id",        res.ToolCallId);
        Assert.Equal("my_tool",   res.Name);
    }

    [Fact]
    public void LlmMessage_AssistantToolCalls_SetsToolCallsAndNullContent()
    {
        var calls = new[]
        {
            new LlmToolCall { Id = "x", Name = "fn", ArgumentsJson = "{}" }
        };
        var msg = LlmMessage.AssistantToolCalls(calls);

        Assert.Equal("assistant", msg.Role);
        Assert.Null(msg.Content);
        Assert.NotNull(msg.ToolCalls);
        Assert.Single(msg.ToolCalls!);
        Assert.Equal("x",  msg.ToolCalls![0].Id);
        Assert.Equal("fn", msg.ToolCalls![0].Name);
    }
}
