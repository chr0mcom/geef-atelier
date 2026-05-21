using System.Text;

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
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return OpenAiMessageFormat.DeserializeResponse(json);
    }
}
