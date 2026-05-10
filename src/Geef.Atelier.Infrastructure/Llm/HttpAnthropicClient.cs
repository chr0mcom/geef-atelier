using System.Text;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Llm;

internal sealed class HttpAnthropicClient(
    HttpClient httpClient,
    IOptions<AnthropicOptions> options) : IAnthropicClient
{
    public async Task<AnthropicResponse> CompleteAsync(
        AnthropicRequest request,
        CancellationToken cancellationToken)
    {
        var apiKey = options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "Anthropic API key is not configured. Set the environment variable 'Anthropic__ApiKey'.");

        var body = AnthropicMessageFormat.SerializeRequest(request);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v1/messages");

        // Set key per-request to avoid leaking it into shared HttpClient headers.
        httpRequest.Headers.Add("x-api-key", apiKey);
        httpRequest.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return AnthropicMessageFormat.DeserializeResponse(json);
    }
}
