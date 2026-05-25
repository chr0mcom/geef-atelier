using System.Net;
using System.Text;
using System.Text.Json;
using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Tests.Llm;

public sealed class OpenAiResponsesClientTests
{
    private static readonly JsonElement EmptySchema =
        JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement;

    private static (OpenAiResponsesClient Client, CapturingHandler Handler) CreateClient(
        string responseJson, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new CapturingHandler(responseJson, status);
        var http = new HttpClient(handler);
        return (new OpenAiResponsesClient(http, "https://api.openai.com/v1", apiKey: "sk-test"), handler);
    }

    [Fact]
    public async Task MapsFunctionCallOutputToToolResponse()
    {
        var responseJson = """
            {
              "id": "resp_1",
              "object": "response",
              "created_at": 1735000000,
              "status": "completed",
              "model": "gpt-5.5-pro",
              "output": [
                {
                  "type": "function_call",
                  "id": "fc_1",
                  "call_id": "call_1",
                  "name": "submit_template_proposal",
                  "arguments": "{\"recommendation\":\"create\"}",
                  "status": "completed"
                }
              ],
              "usage": { "input_tokens": 1200, "output_tokens": 800, "total_tokens": 2000 }
            }
            """;
        var (client, handler) = CreateClient(responseJson);

        var response = await client.CompleteAsync(new LlmRequest
        {
            Model        = "gpt-5.5-pro",
            SystemPrompt = "You are a meta planner.",
            UserPrompt   = "Plan this task.",
            MaxTokens    = 40000,
            Tools        = [new LlmTool { Name = "submit_template_proposal", Description = "Submit a proposal", InputSchema = EmptySchema }],
            ToolChoice   = "function:submit_template_proposal"
        }, CancellationToken.None);

        Assert.Equal("tool_calls", response.FinishReason);
        Assert.Equal("submit_template_proposal", response.ToolName);
        Assert.Equal("""{"recommendation":"create"}""", response.ToolArgumentsJson);
        Assert.Equal(1200, response.TokenUsage.InputTokens);
        Assert.Equal(800, response.TokenUsage.OutputTokens);

        // The outbound request must carry instructions, the tool, and the forced tool choice.
        Assert.Contains("You are a meta planner.", handler.RequestBody);
        Assert.Contains("submit_template_proposal", handler.RequestBody);
    }

    [Fact]
    public async Task MapsMessageOutputToText()
    {
        var responseJson = """
            {
              "id": "resp_2",
              "object": "response",
              "created_at": 1735000001,
              "status": "completed",
              "model": "gpt-4o",
              "output": [
                {
                  "type": "message",
                  "id": "msg_1",
                  "role": "assistant",
                  "status": "completed",
                  "content": [ { "type": "output_text", "text": "Hello from responses.", "annotations": [] } ]
                }
              ],
              "usage": { "input_tokens": 10, "output_tokens": 4, "total_tokens": 14 }
            }
            """;
        var (client, _) = CreateClient(responseJson);

        var response = await client.CompleteAsync(new LlmRequest
        {
            Model        = "gpt-4o",
            SystemPrompt = "You are helpful.",
            UserPrompt   = "Say hello."
        }, CancellationToken.None);

        Assert.Equal("stop", response.FinishReason);
        Assert.Null(response.ToolName);
        Assert.Equal("Hello from responses.", response.Text);
        Assert.Equal(10, response.TokenUsage.InputTokens);
        Assert.Equal(4, response.TokenUsage.OutputTokens);
    }

    [Fact]
    public async Task SurfacesProviderErrorOnFailureStatus()
    {
        var errorJson = """{"error":{"message":"This is not a chat model and thus not supported in the v1/chat/completions endpoint.","type":"invalid_request_error","code":"model_not_found"}}""";
        var (client, _) = CreateClient(errorJson, HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.CompleteAsync(new LlmRequest
            {
                Model        = "gpt-5.5-pro",
                SystemPrompt = "test",
                UserPrompt   = "test"
            }, CancellationToken.None));

        Assert.Contains("gpt-5.5-pro", ex.Message);
        Assert.Contains("not a chat model", ex.Message);
    }

    [Fact]
    public async Task ThrowsOnMissingApiKey()
    {
        var (client, _) = (new OpenAiResponsesClient(new HttpClient(new CapturingHandler("{}", HttpStatusCode.OK)),
            "https://api.openai.com/v1", apiKey: ""), default(CapturingHandler));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.CompleteAsync(new LlmRequest
            {
                Model        = "gpt-4o",
                SystemPrompt = "test",
                UserPrompt   = "test"
            }, CancellationToken.None));
    }

    private sealed class CapturingHandler(string responseJson, HttpStatusCode status) : HttpMessageHandler
    {
        public string RequestBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                RequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
