using System.Collections.Concurrent;
using System.Text.Json;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Providers;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Providers;
using Geef.Atelier.Infrastructure.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Crew;

/// <summary>
/// Fetches available models from each provider's configured models endpoint, caches results for 1 h,
/// and falls back to <see cref="StaticModelFallback"/> when the endpoint is unreachable.
/// Supports HTTP providers with a <c>models_endpoint</c> setting as well as CLI providers whose
/// model list is embedded in their settings.
/// </summary>
internal sealed class ModelCatalog(
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    IServiceScopeFactory scopeFactory,
    ILogger<ModelCatalog> logger) : IModelCatalog
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    // Per-provider semaphores prevent thundering-herd on simultaneous cache misses.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<string, bool> _fallbackFlags = new();

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

            var provider = await LoadProviderAsync(providerName, ct);

            var result = provider is not null
                ? await TryFetchFromProviderAsync(provider, providerName, ct) ?? UseFallback(providerName)
                : UseFallback(providerName);

            cache.Set(CacheKey(providerName), result, CacheTtl);
            return result;
        }
        finally
        {
            sem.Release();
        }
    }

    private async Task<Provider?> LoadProviderAsync(string providerName, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IProviderService>();
        return await service.GetByNameAsync(providerName, ct);
    }

    private async Task<IReadOnlyList<ModelInfo>?> TryFetchFromProviderAsync(
        Provider provider, string providerName, CancellationToken ct)
    {
        if (provider.Type == ProviderType.Cli)
            return BuildCliModels(provider, providerName);

        // HTTP provider
        var settings = HttpProviderSettings.FromSettings(provider.Settings);

        if (settings.ModelsEndpoint is not { Length: > 0 } modelsPath)
        {
            // No live endpoint — use manual list from settings or static fallback.
            if (settings.ManualModelList is { Count: > 0 } manual)
                return BuildFromManualList(manual, providerName);

            return null;
        }

        var endpoint = settings.EndpointEnvOverride is { Length: > 0 } envVar
            ? (Environment.GetEnvironmentVariable(envVar) ?? settings.Endpoint)
            : settings.Endpoint;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            logger.LogWarning("ModelCatalog: provider '{Provider}' has no configured endpoint; using fallback.", providerName);
            return null;
        }

        var modelsUrl = endpoint.TrimEnd('/') + modelsPath;
        return await FetchFromUrlAsync(modelsUrl, settings, providerName, ct);
    }

    private async Task<IReadOnlyList<ModelInfo>?> FetchFromUrlAsync(
        string modelsUrl, HttpProviderSettings settings, string providerName, CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

            var http = httpClientFactory.CreateClient(HttpClientNames.Llm);
            using var request = new HttpRequestMessage(HttpMethod.Get, modelsUrl);

            if (settings.ApiKeyEnv is { Length: > 0 } keyEnv)
            {
                var apiKey = Environment.GetEnvironmentVariable(keyEnv) ?? string.Empty;
                if (apiKey.Length > 0)
                {
                    var headerValue = settings.AuthHeaderFormat.Replace("{key}", apiKey);
                    request.Headers.TryAddWithoutValidation(settings.AuthHeaderName, headerValue);
                }
            }

            foreach (var (header, value) in settings.DefaultHeaders)
                request.Headers.TryAddWithoutValidation(header, value);

            using var response = await http.SendAsync(request, timeoutCts.Token);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
            var root = await JsonSerializer.DeserializeAsync<ModelListResponse>(stream, JsonOpts, timeoutCts.Token);
            if (root?.Data is not { Count: > 0 } data)
                return null;

            var recommendations = BuildRecommendationLookup(providerName);
            var models = data
                .Where(d => !string.IsNullOrWhiteSpace(d.Id))
                .Select(d =>
                {
                    recommendations.TryGetValue(d.Id!, out var rec);
                    return new ModelInfo(
                        Id: d.Id!,
                        DisplayName: rec?.DisplayName ?? d.Id!,
                        Description: rec?.Description,
                        IsRecommended: rec?.IsRecommended ?? false);
                })
                .OrderByDescending(m => m.IsRecommended)
                .ThenBy(m => m.Id)
                .ToList();

            _fallbackFlags[providerName] = false;
            logger.LogInformation("ModelCatalog: fetched {Count} models for '{Provider}'.", models.Count, providerName);
            return models;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timed out (not cancelled by caller) — fall back silently.
            logger.LogWarning("ModelCatalog: timeout fetching models for '{Provider}' from {Url}; will use fallback.",
                providerName, modelsUrl);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "ModelCatalog: failed to fetch models for '{Provider}' from {Url}; will use fallback.",
                providerName, modelsUrl);
            return null;
        }
    }

    private static IReadOnlyList<ModelInfo> BuildCliModels(Provider provider, string providerName)
    {
        var cliSettings = CliProviderSettings.FromSettings(provider.Settings);
        if (cliSettings.Models is { Count: > 0 } models)
        {
            var recommendations = BuildRecommendationLookup(providerName);
            return models.Select(id =>
            {
                recommendations.TryGetValue(id, out var rec);
                return new ModelInfo(
                    Id: id,
                    DisplayName: rec?.DisplayName ?? id,
                    Description: rec?.Description,
                    IsRecommended: rec?.IsRecommended ?? false);
            }).ToList();
        }

        return StaticModelFallback.For(providerName);
    }

    private static IReadOnlyList<ModelInfo> BuildFromManualList(IReadOnlyList<string> manual, string providerName)
    {
        var recommendations = BuildRecommendationLookup(providerName);
        return manual.Select(id =>
        {
            recommendations.TryGetValue(id, out var rec);
            return new ModelInfo(
                Id: id,
                DisplayName: rec?.DisplayName ?? id,
                Description: rec?.Description,
                IsRecommended: rec?.IsRecommended ?? false);
        }).ToList();
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
