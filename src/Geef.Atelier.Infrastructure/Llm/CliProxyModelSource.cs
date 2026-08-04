using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Llm;

/// <inheritdoc />
internal sealed class CliProxyModelSource(
    IHttpClientFactory httpClientFactory,
    IOptions<CliProxyOptions> options,
    ILogger<CliProxyModelSource> logger) : ICliModelSource
{
    private const string LiveSourceMarker = "live";
    private const string StaticSourceMarker = "static";

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    /// <inheritdoc />
    public async Task<CliModelCatalog?> TryGetCatalogAsync(
        string providerName, TimeSpan timeout, bool bypassProxyCache, CancellationToken ct = default)
    {
        var baseUrl = options.Value.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            logger.LogWarning("CliProxyModelSource: no CliProxy:BaseUrl configured; cannot list models for '{Provider}'.", providerName);
            return null;
        }

        var url = $"{baseUrl.TrimEnd('/')}/v1/cli/{Uri.EscapeDataString(providerName)}/models"
                  + (bypassProxyCache ? "?refresh=1" : string.Empty);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);

            var http = httpClientFactory.CreateClient(HttpClientNames.CliProxyModels);
            using var response = await http.GetAsync(url, timeoutCts.Token);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
            var payload = await JsonSerializer.DeserializeAsync<CliModelsResponse>(stream, JsonOpts, timeoutCts.Token);

            var ids = payload?.Data?
                .Select(entry => entry.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .ToList();

            if (ids is not { Count: > 0 })
            {
                logger.LogWarning("CliProxyModelSource: '{Provider}' returned no usable model ids from {Url}.", providerName, url);
                return null;
            }

            var hasLiveSource = !string.Equals(payload!.Source, StaticSourceMarker, StringComparison.OrdinalIgnoreCase);
            var isLive = string.Equals(payload.Source, LiveSourceMarker, StringComparison.OrdinalIgnoreCase);

            if (hasLiveSource && !isLive)
                logger.LogWarning(
                    "CliProxyModelSource: '{Provider}' reported source '{Source}' — the gateway could not resolve the list from the CLI.",
                    providerName, payload.Source);

            return new CliModelCatalog(
                ModelIds: ids,
                IsLive: isLive,
                HasLiveSource: hasLiveSource,
                AliasMap: payload.Aliases ?? new Dictionary<string, string>());
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("CliProxyModelSource: timeout listing models for '{Provider}' from {Url}.", providerName, url);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "CliProxyModelSource: failed to list models for '{Provider}' from {Url}.", providerName, url);
            return null;
        }
    }

    private sealed class CliModelsResponse
    {
        public List<CliModelEntry>? Data { get; set; }

        [JsonPropertyName("x_source")]
        public string? Source { get; set; }

        [JsonPropertyName("x_aliases")]
        public Dictionary<string, string>? Aliases { get; set; }
    }

    private sealed class CliModelEntry
    {
        public string? Id { get; set; }
    }
}
