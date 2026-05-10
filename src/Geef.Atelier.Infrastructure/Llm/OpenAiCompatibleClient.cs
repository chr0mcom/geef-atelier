using System.Text;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Llm;

internal sealed class OpenAiCompatibleClient(
    HttpClient httpClient,
    IOptions<LlmOptions> options) : ILlmClient
{
    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        var apiKey = options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "LLM API key is not configured. Set the environment variable 'Llm__ApiKey'.");

        var endpoint = options.Value.Endpoint.TrimEnd('/');
        var body = OpenAiMessageFormat.SerializeRequest(request);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/chat/completions");

        // Set key per-request to avoid leaking it into shared HttpClient headers.
        httpRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
        httpRequest.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return OpenAiMessageFormat.DeserializeResponse(json);
    }
}
