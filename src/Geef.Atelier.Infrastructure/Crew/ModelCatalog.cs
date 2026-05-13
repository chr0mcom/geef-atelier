using System.Collections.Concurrent;
using System.Text.Json;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Infrastructure.Llm;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Crew;

/// <summary>
/// Fetches available models from each provider's <c>/models</c> endpoint, caches results for 24 h,
/// and falls back to <see cref="StaticModelFallback"/> when the endpoint is unreachable.
/// </summary>
internal sealed class ModelCatalog(
    IHttpClientFactory               httpClientFactory,
    IMemoryCache                     cache,
    IOptions<LlmOptions>             options,
    ILogger<ModelCatalog>            logger) : IModelCatalog
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    // Per-provider semaphores prevent thundering-herd on simultaneous cache misses.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks  = new();
    private readonly ConcurrentDictionary<string, bool>          _fallbackFlags = new();

    private static string CacheKey(string providerName) => $"model-catalog:{providerName}";

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(string providerName, CancellationToken ct = default)
    {
        if (cache.TryGetValue(CacheKey(providerName), out IReadOnlyList<ModelInfo>? cached) && cached is not null)
            return cached;

        return await FetchAndCacheAsync(providerName, ct);
    }

    public async Task<IReadOnlyList<ModelInfo>> RefreshAsync(string providerName, CancellationToken ct = default)
    {
        cache.Remove(CacheKey(providerName));
        return await FetchAndCacheAsync(providerName, ct);
    }

    public bool IsUsingFallback(string providerName) =>
        _fallbackFlags.TryGetValue(providerName, out var v) && v;

    private async Task<IReadOnlyList<ModelInfo>> FetchAndCacheAsync(string providerName, CancellationToken ct)
    {
        var sem = _locks.GetOrAdd(providerName, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            // Double-checked: another thread may have populated the cache while we waited.
            if (cache.TryGetValue(CacheKey(providerName), out IReadOnlyList<ModelInfo>? cached) && cached is not null)
                return cached;

            var result = await TryFetchFromEndpointAsync(providerName, ct)
                         ?? UseFallback(providerName);

            cache.Set(CacheKey(providerName), result, CacheTtl);
            return result;
        }
        finally
        {
            sem.Release();
        }
    }

    private async Task<IReadOnlyList<ModelInfo>?> TryFetchFromEndpointAsync(string providerName, CancellationToken ct)
    {
        if (!options.Value.Providers.TryGetValue(providerName, out var providerCfg))
        {
            logger.LogWarning("ModelCatalog: unknown provider '{Provider}'; using fallback.", providerName);
            return null;
        }

        var modelsUrl = providerCfg.Endpoint.TrimEnd('/') + "/models";
        try
        {
            var http = httpClientFactory.CreateClient("llm");
            using var response = await http.GetAsync(modelsUrl, ct);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            var root = await JsonSerializer.DeserializeAsync<ModelListResponse>(stream, JsonOpts, ct);
            if (root?.Data is not { Count: > 0 } data)
                return null;

            var recommendations = BuildRecommendationLookup(providerName);
            var models = data
                .Where(d => !string.IsNullOrWhiteSpace(d.Id))
                .Select(d =>
                {
                    recommendations.TryGetValue(d.Id!, out var rec);
                    return new ModelInfo(
                        Id:            d.Id!,
                        DisplayName:   rec?.DisplayName ?? d.Id!,
                        Description:   rec?.Description,
                        IsRecommended: rec?.IsRecommended ?? false);
                })
                .OrderByDescending(m => m.IsRecommended)
                .ThenBy(m => m.Id)
                .ToList();

            _fallbackFlags[providerName] = false;
            logger.LogInformation("ModelCatalog: fetched {Count} models for '{Provider}'.", models.Count, providerName);
            return models;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "ModelCatalog: failed to fetch models for '{Provider}' from {Url}; will use fallback.", providerName, modelsUrl);
            return null;
        }
    }

    private IReadOnlyList<ModelInfo> UseFallback(string providerName)
    {
        var fallback = StaticModelFallback.For(providerName);
        _fallbackFlags[providerName] = true;
        logger.LogInformation("ModelCatalog: using static fallback ({Count} models) for '{Provider}'.", fallback.Count, providerName);
        return fallback;
    }

    private static Dictionary<string, ModelInfo> BuildRecommendationLookup(string providerName) =>
        StaticModelFallback.For(providerName)
            .ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);

    // DTO for JSON deserialization — matches OpenAI /models response schema.
    private sealed class ModelListResponse
    {
        public List<ModelEntry>? Data { get; set; }
    }

    private sealed class ModelEntry
    {
        public string? Id { get; set; }
    }
}
