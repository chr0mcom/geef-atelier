using System.Text;
using System.Text.Json;
using Geef.Atelier.Core.Domain.Warnings;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Llm;

internal sealed class OpenAiCompatibleClient(
    HttpClient httpClient,
    string endpoint,
    string? apiKey,
    ILogger? logger = null) : ILlmClient
{
    /// <summary>Header the cli-proxy sets when another CLI served the request (e.g. <c>codex->claude</c>).</summary>
    internal const string FailoverHeader = "x-cli-proxy-failover";

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

        await ReportFailoverAsync(response, request.Model);

        return OpenAiMessageFormat.DeserializeResponse(json);
    }

    /// <summary>
    /// A cli-proxy failover silently runs the request on the partner CLI's default model, which can put a
    /// reviewer onto the executor's model family (D-066). The response still names the requested model, so the
    /// header is the only trace — log it and record it as a warning on the run executing on this async flow
    /// (<see cref="RunWarningScope"/>), once per hop and requested model.
    /// </summary>
    private async Task ReportFailoverAsync(HttpResponseMessage response, string requestedModel)
    {
        if (!response.Headers.TryGetValues(FailoverHeader, out var values))
            return;

        var hops = values.ToList();
        if (hops.Count > 0 && hops.All(IsSameCliHop))
            return;

        var failover = string.Join(",", hops);
        logger?.LogWarning(
            "cli-proxy failover {Failover}: request for model '{Model}' was served by another CLI with its default model; model plurality may not hold for this call.",
            failover, requestedModel);

        if (RunWarningScope.Current is { } scope)
            await scope.ReportOnceAsync(
                $"{RunWarningCodes.LlmFailover}|{failover}|{requestedModel}",
                RunWarningSources.Llm,
                RunWarningCodes.LlmFailover,
                $"An AI service was unavailable, so another one answered instead ({failover.ToWarningDetail()}; requested model {requestedModel.ToWarningDetail()}). Some checks may have used the same model as the writer.");
    }

    /// <summary>
    /// True for a hop such as <c>claude->claude-alt</c>: a second account of the same CLI keeps the model name, so model
    /// plurality is unaffected. Unknown formats count as a CLI change and still warn.
    /// </summary>
    private static bool IsSameCliHop(string hop)
    {
        var parts = hop.Split("->", StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            return false;
        return string.Equals(CliFamily(parts[0]), CliFamily(parts[1]), StringComparison.OrdinalIgnoreCase);
    }

    private static string CliFamily(string backend)
    {
        var dash = backend.IndexOf('-');
        return dash > 0 ? backend[..dash] : backend;
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
