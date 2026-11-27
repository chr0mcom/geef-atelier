using System.Text.Json;
using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Tests.Infrastructure.Llm;

/// <summary>What <see cref="OpenAiMessageFormat"/> sends for a request and reads from an answer with and without tool calls.</summary>
public sealed class OpenAiMessageFormatTests
{
    private static string Answer(string message, string finishReason) => $$"""
        {
          "choices": [{ "message": {{message}}, "finish_reason": "{{finishReason}}" }],
          "usage": { "prompt_tokens": 10, "completion_tokens": 5 }
        }
        """;

    [Fact]
    public void A_request_sends_exactly_the_system_and_the_user_prompt()
    {
        var json = OpenAiMessageFormat.SerializeRequest(new LlmRequest { Model = "test-model", SystemPrompt = "sys", UserPrompt = "user" });

        using var document = JsonDocument.Parse(json);
        var messages = document.RootElement.GetProperty("messages").EnumerateArray()
            .Select(m => (m.GetProperty("role").GetString(), m.GetProperty("content").GetString())).ToList();
        Assert.Equal([("system", "sys"), ("user", "user")], messages);
    }

    [Fact]
    public void The_first_tool_call_of_an_answer_is_read()
    {
        var response = OpenAiMessageFormat.DeserializeResponse(Answer("""
            { "role": "assistant", "content": null, "tool_calls": [
              { "id": "c1", "type": "function", "function": { "name": "first", "arguments": "{\"a\":1}" } },
              { "id": "c2", "type": "function", "function": { "name": "second", "arguments": "{}" } }
            ] }
            """, "tool_calls"));

        Assert.Equal(("first", "{\"a\":1}"), (response.ToolName, response.ToolArgumentsJson));
        Assert.Equal(string.Empty, response.Text);
        Assert.Equal("tool_calls", response.FinishReason);
    }

    [Fact]
    public void An_answer_without_tool_calls_names_no_tool()
    {
        var response = OpenAiMessageFormat.DeserializeResponse(Answer("""{ "role": "assistant", "content": "Hello." }""", "stop"));

        Assert.Equal("Hello.", response.Text);
        Assert.Null(response.ToolName);
        Assert.Null(response.ToolArgumentsJson);
    }
}
