using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Tools.FactCheckEval;

/// <summary>
/// Sends the calls of a subscription CLI to its gateway route (<c>{gateway}/v1/claude</c>, <c>{gateway}/v1/codex</c>), with the
/// same model normalisation and token default as <c>LlmClientResolver</c> and structured outputs on, as for the cli-proxy in
/// production. The key comes only from the environment (<see cref="CommandLine.ApiKeyVariable"/>) and never appears in a log.
/// </summary>
internal sealed class GatewayResolver(HttpClient http, string gateway, string? apiKey) : ILlmClientResolver
{
    /// <summary>The gateway route of a subscription CLI provider; other providers are not measured.</summary>
    public static string Route(string gateway, string provider) => gateway.TrimEnd('/') + provider switch
    {
        ModelMatrix.ClaudeCli => "/v1/claude",
        ModelMatrix.CodexCli => "/v1/codex",
        _ => throw new UsageException($"provider {provider} is not served by the gateway; only {ModelMatrix.ClaudeCli} and {ModelMatrix.CodexCli} are measured"),
    };

    public (ILlmClient Client, string Model, int MaxTokens) ForActor(string actorName) =>
        throw new NotSupportedException("The measurement uses profiles only.");

    public (ILlmClient Client, string Model, int MaxTokens) ForProfile(string provider, string model, int? maxTokens) =>
        (new OpenAiCompatibleClient(http, Route(gateway, provider), apiKey), ModelNameNormalizer.Normalize(provider, model), maxTokens ?? new LlmOptions().DefaultMaxTokens);

    public bool SupportsAgenticTools(string providerName) => false;

    public bool SupportsStructuredOutputs(string providerName) => true;
}
