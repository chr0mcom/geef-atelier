using System.Net;
using System.Text;
using System.Text.Json;
using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Tests.Llm;

public sealed class OpenAiCompatibleClientTests
{
    private static HttpClient CreateMockClient(string responseJson, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new MockHttpMessageHandler(responseJson, status);
        return new HttpClient(handler) { BaseAddress = new Uri("https://openrouter.ai/api/v1/") };
    }

    [Fact]
    public async Task HandlesToolCallResponse()
    {
        var responseJson = """
            {
              "choices": [{
                "message": {
                  "role": "assistant",
                  "content": null,
                  "tool_calls": [{
                    "id": "call_abc",
                    "type": "function",
                    "function": {
                      "name": "submit_review",
                      "arguments": "{\"approved\":false,\"findings\":[{\"severity\":\"warning\",\"message\":\"Test finding\"}]}"
                    }
                  }]
                },
                "finish_reason": "tool_calls"
              }],
              "usage": {
                "prompt_tokens": 100,
                "completion_tokens": 50,
                "total_tokens": 150
              }
            }
            """;

        var client = new OpenAiCompatibleClient(CreateMockClient(responseJson), "https://openrouter.ai/api/v1", "test-key");
        var response = await client.CompleteAsync(new LlmRequest
        {
            Model        = "anthropic/claude-opus-4.7",
            SystemPrompt = "You are a reviewer.",
            UserPrompt   = "Review this."
        }, CancellationToken.None);

        Assert.Equal("submit_review", response.ToolName);
        Assert.NotNull(response.ToolArgumentsJson);
        // ToolArgumentsJson is a raw JSON string — verify it can be parsed
        using var doc = JsonDocument.Parse(response.ToolArgumentsJson);
        Assert.False(doc.RootElement.GetProperty("approved").GetBoolean());
        Assert.Equal("tool_calls", response.FinishReason);
        Assert.Equal(100, response.TokenUsage.InputTokens);
        Assert.Equal(50, response.TokenUsage.OutputTokens);
    }

    [Fact]
    public async Task HandlesPlainTextResponse()
    {
        var responseJson = """
            {
              "choices": [{
                "message": {
                  "role": "assistant",
                  "content": "This is the generated text."
                },
                "finish_reason": "stop"
              }],
              "usage": {
                "prompt_tokens": 42,
                "completion_tokens": 18,
                "total_tokens": 60
              }
            }
            """;

        var client = new OpenAiCompatibleClient(CreateMockClient(responseJson), "https://openrouter.ai/api/v1", "test-key");
        var response = await client.CompleteAsync(new LlmRequest
        {
            Model        = "anthropic/claude-opus-4.7",
            SystemPrompt = "You are a writer.",
            UserPrompt   = "Write something."
        }, CancellationToken.None);

        Assert.Equal("This is the generated text.", response.Text);
        Assert.Null(response.ToolName);
        Assert.Null(response.ToolArgumentsJson);
        Assert.Equal("stop", response.FinishReason);
        Assert.Equal(42, response.TokenUsage.InputTokens);
        Assert.Equal(18, response.TokenUsage.OutputTokens);
    }

    [Fact]
    public async Task ThrowsOnEmptyApiKey()
    {
        var responseJson = "{}"; // irrelevant — should not be called
        var client = new OpenAiCompatibleClient(CreateMockClient(responseJson), "https://openrouter.ai/api/v1", apiKey: "");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.CompleteAsync(new LlmRequest
            {
                Model        = "test",
                SystemPrompt = "test",
                UserPrompt   = "test"
            }, CancellationToken.None));
    }

    private sealed class MockHttpMessageHandler(string responseJson, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var content = new StringContent(responseJson, Encoding.UTF8, "application/json");
            return Task.FromResult(new HttpResponseMessage(status) { Content = content });
        }
    }
}
