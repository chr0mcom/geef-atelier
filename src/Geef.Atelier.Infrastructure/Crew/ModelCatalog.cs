using System.Collections.Concurrent;
using System.Text.Json;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Providers;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Providers;
using Geef.Atelier.Infrastructure.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Crew;

/// <summary>
/// Serves the model list of each provider from its live source where one exists, caching results and
/// degrading to <see cref="StaticModelFallback"/> when a lookup fails. HTTP providers are read from
/// their configured models endpoint; CLI providers are read from the cli-proxy sidecar, which is the
/// only component that can ask a CLI which models it currently offers.
/// </summary>
internal sealed class ModelCatalog(
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    IServiceScopeFactory scopeFactory,
    ICliModelSource cliModelSource,
    IOptions<ModelCatalogOptions> options,
    TimeProvider timeProvider,
    ILogger<ModelCatalog> logger) : IModelCatalog
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Marks a provider as having no alias layer, so resolution short-circuits for it.</summary>
    private static readonly IReadOnlyDictionary<string, string> EmptyAliasMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>An alias mapping together with the instant it stops being trustworthy.</summary>
    private sealed record AliasMapEntry(IReadOnlyDictionary<string, string> Map, DateTimeOffset ExpiresAt);

    /// <summary>Per-provider semaphores, so simultaneous cache misses cause one lookup, not a herd.</summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    /// <summary>
    /// Provenance per provider, held next to the cache rather than inside it: an eviction between
    /// <see cref="ListModelsAsync"/> and <see cref="GetSource"/> would otherwise reset the source to
    /// Unknown while the UI is still rendering the list it just received.
    /// </summary>
    private readonly ConcurrentDictionary<string, ModelCatalogSource> _sources = new();

    /// <summary>
    /// Alias-to-concrete mapping per provider, with its own expiry.
    /// </summary>
    /// <remarks>
    /// Kept across a failed lookup on purpose: an unresolved alias goes into an immutable run
    /// snapshot and cannot be repaired later, whereas a slightly old mapping heals itself on the
    /// next success. The expiry is explicit rather than derived from the cached list, because the
    /// cache is keyed per provider and not per generation — a failed lookup writes its own entry
    /// under the same key, so "a list is cached" is true exactly when it must not be trusted.
    /// Written by encoding, never by value shape; the rule is in geef_architecture.md section 9.3.
    /// </remarks>
    private readonly ConcurrentDictionary<string, AliasMapEntry> _aliasMaps = new();

    private enum FetchBudget
    {
        Interactive,
        Refresh,
        WarmUp,
    }

    private sealed record CatalogResult(IReadOnlyList<ModelInfo> Models, ModelCatalogSource Source);

    private static string CacheKey(string providerName) => $"model-catalog:{providerName}";

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(string providerName, CancellationToken ct = default)
    {
        if (cache.TryGetValue(CacheKey(providerName), out IReadOnlyList<ModelInfo>? cached) && cached is not null)
            return cached;

        return await FetchAndCacheAsync(providerName, FetchBudget.Interactive, bypassBackendCache: false, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModelInfo>> RefreshAsync(string providerName, CancellationToken ct = default) =>
        await FetchAndCacheAsync(providerName, FetchBudget.Refresh, bypassBackendCache: true, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModelInfo>> WarmUpAsync(string providerName, CancellationToken ct = default) =>
        await FetchAndCacheAsync(providerName, FetchBudget.WarmUp, bypassBackendCache: true, ct);

    /// <inheritdoc />
    public ModelCatalogSource GetSource(string providerName) =>
        _sources.TryGetValue(providerName, out var source) ? source : ModelCatalogSource.Unknown;

    /// <inheritdoc />
    public bool IsUsingFallback(string providerName) =>
        GetSource(providerName) == ModelCatalogSource.Fallback;

    /// <inheritdoc />
    /// <remarks>
    /// Deliberately never triggers a lookup. This runs on the run-start path, once per actor, and a
    /// network round-trip there would put the shared LLM client — and its circuit breaker — in front
    /// of every run. The mapping is kept fresh by the warm-up service and by every visit to the
    /// picker, and is trusted only until its own expiry.
    /// </remarks>
    public Task<string> ResolveModelAsync(string providerName, string modelId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return Task.FromResult(modelId);

        if (_aliasMaps.TryGetValue(providerName, out var entry)
            && timeProvider.GetUtcNow() < entry.ExpiresAt
            && entry.Map.TryGetValue(modelId, out var resolved)
            && !string.IsNullOrWhiteSpace(resolved))
        {
            return Task.FromResult(resolved);
        }

        return Task.FromResult(modelId);
    }

    /// <summary>
    /// Serialises lookups per provider and caches the result. The cache is re-checked inside the
    /// semaphore because another caller may have populated it while this one waited.
    /// </summary>
    /// <remarks>
    /// One deadline covers the whole call. A single linked token carries it into queueing for the
    /// provider lock, loading the provider and the lookup itself, so no stage — not even a hanging
    /// database — can outlive the configured budget, and an interactive caller can neither inherit
    /// the far larger budget a refresh or warm-up holds the lock for nor spend its own twice over.
    /// Running out of budget is the absence of a result, not a failed lookup: it serves what is
    /// already known and deliberately leaves the recorded provenance untouched, so a slow neighbour
    /// cannot make the picker claim the provider is unreachable.
    /// </remarks>
    private async Task<IReadOnlyList<ModelInfo>> FetchAndCacheAsync(
        string providerName, FetchBudget budget, bool bypassBackendCache, CancellationToken ct)
    {
        var deadline = timeProvider.GetUtcNow() + TimeoutFor(budget);

        using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budgetCts.CancelAfter(TimeoutFor(budget));
        var budgetToken = budgetCts.Token;

        var sem = _locks.GetOrAdd(providerName, _ => new SemaphoreSlim(1, 1));

        try
        {
            if (!await sem.WaitAsync(Remaining(deadline), budgetToken))
                return OutOfBudget(providerName);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return OutOfBudget(providerName);
        }

        try
        {
            if (!bypassBackendCache
                && cache.TryGetValue(CacheKey(providerName), out IReadOnlyList<ModelInfo>? cached)
                && cached is not null)
            {
                return cached;
            }

            Provider? provider;
            CatalogResult result;
            try
            {
                provider = await LoadProviderAsync(providerName, budgetToken);

                result = provider is not null
                    ? await BuildCatalogAsync(provider, providerName, Remaining(deadline), bypassBackendCache, budgetToken)
                    : new CatalogResult(StaticModelFallback.For(providerName), ModelCatalogSource.Fallback);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return OutOfBudget(providerName);
            }

            _sources[providerName] = result.Source;
            cache.Set(CacheKey(providerName), result.Models, TtlFor(provider, result.Source));
            return result.Models;
        }
        finally
        {
            sem.Release();
        }
    }

    private TimeSpan TtlFor(Provider? provider, ModelCatalogSource source)
    {
        var opts = options.Value;

        if (source == ModelCatalogSource.Fallback)
            return TimeSpan.FromMinutes(Math.Max(1, opts.FailureTtlMinutes));

        return provider?.Type == ProviderType.Cli
            ? TimeSpan.FromHours(Math.Max(1, opts.CliTtlHours))
            : TimeSpan.FromHours(Math.Max(1, opts.HttpTtlHours));
    }

    /// <summary>Budget left before the deadline; never negative.</summary>
    private TimeSpan Remaining(DateTimeOffset deadline)
    {
        var left = deadline - timeProvider.GetUtcNow();
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    /// <summary>
    /// Serves what is already known when the budget runs out. Deliberately records no source:
    /// running out of time is not evidence that the provider is unreachable.
    /// </summary>
    private IReadOnlyList<ModelInfo> OutOfBudget(string providerName)
    {
        logger.LogWarning("ModelCatalog: budget exhausted for '{Provider}'; serving what is known.", providerName);

        return cache.TryGetValue(CacheKey(providerName), out IReadOnlyList<ModelInfo>? known) && known is not null
            ? known
            : StaticModelFallback.For(providerName);
    }

    private TimeSpan TimeoutFor(FetchBudget budget)
    {
        var opts = options.Value;
        return budget switch
        {
            FetchBudget.WarmUp  => TimeSpan.FromSeconds(Math.Max(1, opts.WarmUpTimeoutSeconds)),
            FetchBudget.Refresh => TimeSpan.FromSeconds(Math.Max(1, opts.RefreshTimeoutSeconds)),
            _                   => TimeSpan.FromSeconds(Math.Max(1, opts.InteractiveTimeoutSeconds)),
        };
    }

    private async Task<Provider?> LoadProviderAsync(string providerName, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IProviderService>();
        return await service.GetByNameAsync(providerName, ct);
    }

    /// <summary>
    /// Builds the catalog of an HTTP provider, or delegates to the CLI path. A manual model list on
    /// a provider without a models endpoint is authoritative, not a degradation.
    /// </summary>
    private async Task<CatalogResult> BuildCatalogAsync(
        Provider provider, string providerName, TimeSpan remaining, bool bypassBackendCache, CancellationToken ct)
    {
        if (provider.Type == ProviderType.Cli)
            return await BuildCliCatalogAsync(provider, providerName, remaining, bypassBackendCache, ct);

        RecordNoAliasLayer(providerName);

        var settings = HttpProviderSettings.FromSettings(provider.Settings);

        if (settings.ModelsEndpoint is not { Length: > 0 } modelsPath)
        {
            if (settings.ManualModelList is { Count: > 0 } manual)
                return new CatalogResult(BuildModels(manual, providerName), ModelCatalogSource.StaticByDesign);

            return new CatalogResult(StaticModelFallback.For(providerName), ModelCatalogSource.Fallback);
        }

        var endpoint = settings.EndpointEnvOverride is { Length: > 0 } envVar
            ? (Environment.GetEnvironmentVariable(envVar) ?? settings.Endpoint)
            : settings.Endpoint;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            logger.LogWarning("ModelCatalog: provider '{Provider}' has no configured endpoint; using fallback.", providerName);
            return new CatalogResult(StaticModelFallback.For(providerName), ModelCatalogSource.Fallback);
        }

        var modelsUrl = endpoint.TrimEnd('/') + modelsPath;
        var ids = await FetchFromUrlAsync(modelsUrl, settings, providerName, ct);

        if (ids is not { Count: > 0 })
            return new CatalogResult(StaticModelFallback.For(providerName), ModelCatalogSource.Fallback);

        var models = BuildModels(ids, providerName)
            .OrderByDescending(m => m.IsRecommended)
            .ThenBy(m => m.Id)
            .ToList();

        logger.LogInformation("ModelCatalog: fetched {Count} models for '{Provider}'.", models.Count, providerName);
        return new CatalogResult(models, ModelCatalogSource.Live);
    }

    /// <summary>
    /// Builds the catalog of a CLI provider. The alias map is written by encoding, never by value
    /// shape: a degraded answer is an HTTP 200 with an empty map and must leave the previous mapping
    /// standing, exactly like a transport failure (geef_architecture.md section 9.3).
    /// </summary>
    private async Task<CatalogResult> BuildCliCatalogAsync(
        Provider provider, string providerName, TimeSpan remaining, bool bypassBackendCache, CancellationToken ct)
    {
        var cliSettings = CliProviderSettings.FromSettings(provider.Settings);
        var configured = cliSettings.Models is { Count: > 0 }
            ? cliSettings.Models
            : StaticModelFallback.For(providerName).Select(m => m.Id).ToList();

        if (!options.Value.CliLiveCatalogEnabled)
        {
            RecordNoAliasLayer(providerName);
            return new CatalogResult(
                SortCli(BuildModels(MergeWithKnownIds(configured, providerName), providerName)),
                ModelCatalogSource.StaticByDesign);
        }

        var live = await cliModelSource.TryGetCatalogAsync(providerName, remaining, bypassBackendCache, ct);

        if (live is null)
        {
            return new CatalogResult(
                SortCli(BuildModels(MergeWithKnownIds(configured, providerName), providerName)),
                ModelCatalogSource.Fallback);
        }

        if (live.IsLive)
            RecordResolvedAliases(providerName, live.AliasMap);
        else if (!live.HasLiveSource)
            RecordNoAliasLayer(providerName);

        var merged = MergeWithKnownIds(live.ModelIds, providerName);
        var models = SortCli(BuildModels(merged, providerName));

        var source = live.IsLive
            ? ModelCatalogSource.Live
            : live.HasLiveSource ? ModelCatalogSource.Fallback : ModelCatalogSource.StaticByDesign;

        logger.LogInformation("ModelCatalog: '{Provider}' reported {Count} models ({Source}).",
            providerName, models.Count, source);

        return new CatalogResult(models, source);
    }

    /// <summary>
    /// Records that this provider has no alias layer at all — permanently true, so it never expires.
    /// </summary>
    private void RecordNoAliasLayer(string providerName) =>
        _aliasMaps[providerName] = new AliasMapEntry(EmptyAliasMap, DateTimeOffset.MaxValue);

    /// <summary>
    /// Records a mapping the gateway actually resolved, valid for as long as a successful list is.
    /// </summary>
    /// <remarks>
    /// Merged into what is already known rather than replacing it: the gateway reports a live answer
    /// as soon as one model family resolved, so a partial run must not drop the families that
    /// resolved earlier. Newly reported entries win.
    /// </remarks>
    private void RecordResolvedAliases(string providerName, IReadOnlyDictionary<string, string> aliases)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (_aliasMaps.TryGetValue(providerName, out var known))
        {
            foreach (var (alias, model) in known.Map)
                merged[alias] = model;
        }

        foreach (var (alias, model) in aliases)
            merged[alias] = model;

        _aliasMaps[providerName] = new AliasMapEntry(
            merged.Count > 0 ? merged : EmptyAliasMap,
            timeProvider.GetUtcNow().AddHours(Math.Max(1, options.Value.CliTtlHours)));
    }

    /// <summary>
    /// Appends the curated ids that the backend did not report, rather than letting its answer
    /// replace the list. A curated model that briefly drops out must not silently disappear from the
    /// picker — the composer filters its preferred models against this catalog — and the recommended
    /// group must never end up empty.
    /// </summary>
    private static IReadOnlyList<string> MergeWithKnownIds(IReadOnlyList<string> liveIds, string providerName)
    {
        var merged = new List<string>(liveIds);
        var seen = new HashSet<string>(liveIds, StringComparer.OrdinalIgnoreCase);

        foreach (var known in StaticModelFallback.For(providerName))
        {
            if (seen.Add(known.Id))
                merged.Add(known.Id);
        }

        return merged;
    }

    /// <summary>
    /// Stable sort on <c>IsRecommended</c> only. The backend puts the best model first, and an
    /// alphabetical tie-break would promote the haiku alias to the top of the recommended group —
    /// which the picker auto-selects, quietly making it the default model of every new profile.
    /// </summary>
    private static List<ModelInfo> SortCli(IEnumerable<ModelInfo> models) =>
        models.OrderByDescending(m => m.IsRecommended).ToList();

    private async Task<IReadOnlyList<string>?> FetchFromUrlAsync(
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

            var ids = root?.Data?
                .Select(d => d.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .ToList();

            return ids is { Count: > 0 } ? ids : null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
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

    private static List<ModelInfo> BuildModels(IReadOnlyList<string> ids, string providerName)
    {
        var recommendations = BuildRecommendationLookup(providerName);
        return ids.Select(id =>
        {
            recommendations.TryGetValue(id, out var rec);
            return new ModelInfo(
                Id: id,
                DisplayName: rec?.DisplayName ?? id,
                Description: rec?.Description,
                IsRecommended: rec?.IsRecommended ?? false);
        }).ToList();
    }

    private static Dictionary<string, ModelInfo> BuildRecommendationLookup(string providerName) =>
        StaticModelFallback.For(providerName)
            .ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>Response shape of an OpenAI-compatible <c>/models</c> endpoint.</summary>
    private sealed class ModelListResponse
    {
        public List<ModelEntry>? Data { get; set; }
    }

    private sealed class ModelEntry
    {
        public string? Id { get; set; }
    }
}
