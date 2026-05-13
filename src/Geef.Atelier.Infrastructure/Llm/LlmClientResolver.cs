using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Llm;

internal sealed class LlmClientResolver(
    IHttpClientFactory factory,
    IOptions<LlmOptions> options) : ILlmClientResolver
{
    // One OpenAiCompatibleClient per provider, shared and thread-safe.
    private readonly ConcurrentDictionary<string, ILlmClient> _clients = new();

    public (ILlmClient Client, string Model, int MaxTokens) ForActor(string actorName)
    {
        var opts = options.Value;

        if (!opts.Actors.TryGetValue(actorName, out var actorCfg))
            throw new InvalidOperationException(
                $"Actor '{actorName}' is not configured in Llm:Actors.");

        var providerName = actorCfg.Provider is { Length: > 0 } p ? p : opts.DefaultProvider;

        if (!opts.Providers.TryGetValue(providerName, out var providerCfg))
            throw new InvalidOperationException(
                $"Provider '{providerName}' (used by actor '{actorName}') is not configured in Llm:Providers.");

        var client = _clients.GetOrAdd(providerName, _ =>
            new OpenAiCompatibleClient(factory.CreateClient("llm"), providerCfg.Endpoint, providerCfg.ApiKey));

        if (actorCfg.Model is not { Length: > 0 } model)
            throw new InvalidOperationException(
                $"No model configured for actor '{actorName}' in Llm:Actors.");

        var maxTokens = actorCfg.MaxTokens ?? opts.DefaultMaxTokens;
        return (client, model, maxTokens);
    }

    public (ILlmClient Client, string Model, int MaxTokens) ForProfile(string provider, string model, int? maxTokens)
    {
        var opts = options.Value;

        if (!opts.Providers.TryGetValue(provider, out var providerCfg))
            throw new InvalidOperationException(
                $"Provider '{provider}' (requested by profile) is not configured in Llm:Providers.");

        var client = _clients.GetOrAdd(provider, _ =>
            new OpenAiCompatibleClient(factory.CreateClient("llm"), providerCfg.Endpoint, providerCfg.ApiKey));

        return (client, model, maxTokens ?? opts.DefaultMaxTokens);
    }
}
