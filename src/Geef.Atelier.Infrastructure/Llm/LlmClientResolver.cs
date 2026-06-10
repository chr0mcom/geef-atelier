using System.Collections.Concurrent;
using Geef.Atelier.Application.Providers;
using Geef.Atelier.Core.Domain.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Llm;

internal sealed class LlmClientResolver(
    IHttpClientFactory factory,
    IOptions<LlmOptions> options,
    IServiceScopeFactory scopeFactory) : ILlmClientResolver
{
    // One OpenAiCompatibleClient per provider, shared and thread-safe.
    private readonly ConcurrentDictionary<string, ILlmClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    // Provider lookup cache: avoids re-entering a scope on every call.
    // Lazy<T> with ExecutionAndPublication ensures the factory runs exactly once per provider.
    private readonly ConcurrentDictionary<string, Lazy<Provider?>> _providerCache = new(StringComparer.OrdinalIgnoreCase);

    public (ILlmClient Client, string Model, int MaxTokens) ForActor(string actorName)
    {
        var opts = options.Value;

        if (!opts.Actors.TryGetValue(actorName, out var actorCfg))
            throw new InvalidOperationException(
                $"Actor '{actorName}' is not configured in Llm:Actors.");

        var providerName = actorCfg.Provider is { Length: > 0 } p ? p : opts.DefaultProvider;
        var client = ResolveClient(providerName);

        if (actorCfg.Model is not { Length: > 0 } model)
            throw new InvalidOperationException(
                $"No model configured for actor '{actorName}' in Llm:Actors.");

        return (client, model, actorCfg.MaxTokens ?? opts.DefaultMaxTokens);
    }

    public (ILlmClient Client, string Model, int MaxTokens) ForProfile(string provider, string model, int? maxTokens)
    {
        var client = ResolveClient(provider);
        var normalised = ModelNameNormalizer.Normalize(provider, model);
        return (client, normalised, maxTokens ?? options.Value.DefaultMaxTokens);
    }

    /// <summary>Clears the provider lookup cache so the next call re-fetches from the service.</summary>
    public void InvalidateCache() => _providerCache.Clear();

    /// <inheritdoc/>
    public bool SupportsAgenticTools(string providerName)
    {
        var provider = LoadProvider(providerName);
        return provider?.SupportsAgenticTools() ?? false;
    }


    // ── Private helpers ───────────────────────────────────────────────────────────────

    private ILlmClient ResolveClient(string providerName)
    {
        return _clients.GetOrAdd(providerName, name =>
        {
            var (endpoint, apiKey) = ResolveProviderConnection(name);
            var http = factory.CreateClient(HttpClientNames.Llm);

            // OpenAI's pro/reasoning models (e.g. gpt-5.5-pro) are only served on the Responses API,
            // not /v1/chat/completions. Route any provider that targets api.openai.com accordingly.
            return TargetsOpenAi(endpoint)
                ? new OpenAiResponsesClient(http, endpoint, apiKey)
                : new OpenAiCompatibleClient(http, endpoint, apiKey);
        });
    }

    private static bool TargetsOpenAi(string endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) &&
        uri.Host.Equals("api.openai.com", StringComparison.OrdinalIgnoreCase);

    private (string Endpoint, string? ApiKey) ResolveProviderConnection(string providerName)
    {
        // 1. Try IProviderService (DB custom + system constants).
        var provider = LoadProvider(providerName);

        if (provider is not null && provider.Type == ProviderType.Http)
        {
            var settings = HttpProviderSettings.FromSettings(provider.Settings);

            var endpoint = settings.EndpointEnvOverride is { Length: > 0 } envVar
                ? (Environment.GetEnvironmentVariable(envVar) ?? settings.Endpoint)
                : settings.Endpoint;

            // null  → keyless provider (e.g. Ollama): no auth header, no guard
            // ""    → ApiKeyEnv configured but env var not set: will throw in client
            // "…"   → valid key
            string? apiKey = settings.ApiKeyEnv is { Length: > 0 } keyEnv
                ? (Environment.GetEnvironmentVariable(keyEnv) ?? string.Empty)
                : null;

            return (endpoint, apiKey);
        }

        // 2. Fall back to ProvidersFallback (appsettings) for legacy/CLI-proxy entries.
        // CLI providers route through the cli-proxy; their endpoint is already in ProvidersFallback.
        if (options.Value.ProvidersFallback.TryGetValue(providerName, out var fallback))
            return (fallback.Endpoint, fallback.ApiKey);

        throw new InvalidOperationException(
            $"Provider '{providerName}' is not configured in IProviderService or Llm:ProvidersFallback.");
    }

    private Provider? LoadProvider(string providerName)
    {
        // Singleton resolving a Scoped service: create a short-lived scope.
        // Lazy with ExecutionAndPublication ensures the factory runs exactly once per provider,
        // even under concurrent load (no double-fetch, no race condition on the cache entry).
        var lazy = _providerCache.GetOrAdd(providerName,
            name => new Lazy<Provider?>(() =>
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IProviderService>();
                return service.GetByNameAsync(name, CancellationToken.None).GetAwaiter().GetResult();
            }, LazyThreadSafetyMode.ExecutionAndPublication));
        return lazy.Value;
    }
}
