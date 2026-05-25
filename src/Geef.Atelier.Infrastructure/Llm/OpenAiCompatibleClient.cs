using System.Text;
using System.Text.Json;

namespace Geef.Atelier.Infrastructure.Llm;

internal sealed class OpenAiCompatibleClient(
    HttpClient httpClient,
    string endpoint,
    string? apiKey) : ILlmClient
{
    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        // null  → keyless provider (e.g. Ollama): skip auth header entirely
        // ""    → ApiKeyEnv env var was configured but not set in the environment
        if (apiKey is not null && apiKey.Length == 0)
            throw new InvalidOperationException(
                "LLM API key is not configured. Set the environment variable for the provider's ApiKey.");

        var baseEndpoint = endpoint.TrimEnd('/');
        var body = OpenAiMessageFormat.SerializeRequest(request);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseEndpoint}/chat/completions");

        // Only add auth header when an API key is configured.
        if (apiKey is { Length: > 0 })
            httpRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
        httpRequest.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"LLM request to '{baseEndpoint}' for model '{request.Model}' failed with " +
                $"{(int)response.StatusCode} {response.ReasonPhrase}: {ExtractError(json)}",
                inner: null,
                statusCode: response.StatusCode);

        return OpenAiMessageFormat.DeserializeResponse(json);
    }

    // Surfaces the provider's actual error text (OpenAI-shaped { "error": { "message": ... } })
    // instead of the opaque "status code does not indicate success" default.
    private static string ExtractError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "(empty response body)";

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object &&
                    error.TryGetProperty("message", out var message) &&
                    message.ValueKind == JsonValueKind.String)
                    return message.GetString() ?? body;
                if (error.ValueKind == JsonValueKind.String)
                    return error.GetString() ?? body;
            }
        }
        catch (JsonException) { /* fall through to raw body */ }

        return body.Length > 500 ? body[..500] + "…" : body;
    }
}
