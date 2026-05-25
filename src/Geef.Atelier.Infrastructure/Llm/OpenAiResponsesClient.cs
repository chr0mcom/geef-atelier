using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using OpenAI;
using OpenAI.Responses;

namespace Geef.Atelier.Infrastructure.Llm;

// The OpenAI Responses API surface ships behind the experimental OPENAI001 diagnostic.
// We pin OpenAI 2.8.0 and treat warnings as errors, so suppress it for this file only.
#pragma warning disable OPENAI001

/// <summary>
/// <see cref="ILlmClient"/> backed by OpenAI's Responses API (<c>/v1/responses</c>) via the official SDK.
/// Reasoning/"pro" models (e.g. <c>gpt-5.5-pro</c>) are not served on the legacy <c>/v1/chat/completions</c>
/// endpoint; this client speaks the Responses API instead and handles regular chat models the same way.
/// </summary>
internal sealed class OpenAiResponsesClient(
    HttpClient httpClient,
    string endpoint,
    string? apiKey) : ILlmClient
{
    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException(
                "OpenAI API key is not configured. Set the provider's ApiKey environment variable (e.g. OPENAI_API_KEY).");

        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(endpoint.TrimEnd('/')),
            // Route through the shared, resilience-wrapped "llm" HttpClient instead of the SDK's own transport.
            Transport = new HttpClientPipelineTransport(httpClient),
            // The HttpClient already carries a Polly retry handler; disable the SDK's duplicate retries.
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
            // The SDK's per-attempt default is 100 s. Reasoning models stream well past that, so defer the
            // real cap to the caller's CancellationToken (Studio: 600 s) and the HttpClient timeout (30 min).
            NetworkTimeout = TimeSpan.FromMinutes(30),
        };

        var client = new ResponsesClient(request.Model, new ApiKeyCredential(apiKey), clientOptions);

        var options = new CreateResponseOptions
        {
            Model               = request.Model,
            Instructions        = request.SystemPrompt,
            MaxOutputTokenCount = request.MaxTokens,
            StoredOutputEnabled = false,
        };
        options.InputItems.Add(ResponseItem.CreateUserMessageItem(request.UserPrompt));

        if (request.Tools is { Count: > 0 })
        {
            foreach (var tool in request.Tools)
                options.Tools.Add(ResponseTool.CreateFunctionTool(
                    functionName:        tool.Name,
                    functionParameters:  BinaryData.FromString(tool.InputSchema.GetRawText()),
                    strictModeEnabled:   null,
                    functionDescription: tool.Description));

            options.ToolChoice = ResolveToolChoice(request.ToolChoice);
        }

        ResponseResult result;
        try
        {
            result = await client.CreateResponseAsync(options, cancellationToken);
        }
        catch (ClientResultException ex)
        {
            throw new HttpRequestException(
                $"OpenAI Responses request to '{endpoint}' for model '{request.Model}' failed with " +
                $"{ex.Status}: {ex.Message}",
                inner: ex,
                statusCode: ex.Status > 0 ? (HttpStatusCode)ex.Status : null);
        }

        if (result.Error is { Message.Length: > 0 } error)
            throw new HttpRequestException(
                $"OpenAI Responses returned an error for model '{request.Model}': {error.Message}");

        return MapResponse(result);
    }

    private static ResponseToolChoice ResolveToolChoice(string? toolChoice)
    {
        if (string.IsNullOrEmpty(toolChoice) || string.Equals(toolChoice, "auto", StringComparison.Ordinal))
            return ResponseToolChoice.CreateAutoChoice();
        if (toolChoice.StartsWith("function:", StringComparison.Ordinal))
            return ResponseToolChoice.CreateFunctionChoice(toolChoice["function:".Length..]);
        if (string.Equals(toolChoice, "required", StringComparison.Ordinal))
            return ResponseToolChoice.CreateRequiredChoice();
        return ResponseToolChoice.CreateAutoChoice();
    }

    private static LlmResponse MapResponse(ResponseResult result)
    {
        FunctionCallResponseItem? functionCall = null;
        var text = new StringBuilder();

        foreach (var item in result.OutputItems)
        {
            switch (item)
            {
                case FunctionCallResponseItem fc:
                    functionCall ??= fc;
                    break;
                case MessageResponseItem message:
                    foreach (var part in message.Content)
                        if (part.Text is { Length: > 0 } partText)
                            text.Append(partText);
                    break;
            }
        }

        var usage = result.Usage;
        var tokenUsage = new LlmTokenUsage
        {
            InputTokens  = usage?.InputTokenCount  ?? 0,
            OutputTokens = usage?.OutputTokenCount ?? 0,
        };

        if (functionCall is not null)
            return new LlmResponse
            {
                Text              = text.ToString(),
                ToolName          = functionCall.FunctionName,
                ToolArgumentsJson = functionCall.FunctionArguments?.ToString(),
                TokenUsage        = tokenUsage,
                FinishReason      = "tool_calls",
            };

        return new LlmResponse
        {
            Text         = text.ToString(),
            TokenUsage   = tokenUsage,
            FinishReason = result.IncompleteStatusDetails?.Reason is not null ? "length" : "stop",
        };
    }
}
