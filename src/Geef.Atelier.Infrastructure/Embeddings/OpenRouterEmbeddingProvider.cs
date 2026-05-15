using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Application.Crew.Knowledge.Options;
using Geef.Atelier.Infrastructure.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Embeddings;

/// <summary>
/// Embedding provider backed by the OpenRouter /embeddings endpoint.
/// Uses the openrouter API key from <see cref="LlmOptions.Providers"/>.
/// </summary>
internal sealed class OpenRouterEmbeddingProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<EmbeddingsOptions> options,
    IOptions<LlmOptions> llmOptions,
    ILogger<OpenRouterEmbeddingProvider> logger) : IEmbeddingProvider
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private EmbeddingsOptions Opts => options.Value;

    /// <inheritdoc/>
    public string ProviderName => "openrouter";

    /// <inheritdoc/>
    public string ModelName => Opts.Model;

    /// <inheritdoc/>
    public int Dimensions => Opts.Dimensions;

    /// <inheritdoc/>
    public async Task<EmbeddingResult> CreateAsync(string text, CancellationToken ct)
    {
        var results = await CreateBatchAsync([text], ct);
        return results[0];
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<EmbeddingResult>> CreateBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct)
    {
        if (texts.Count == 0)
            return [];

        var opts = Opts;

        if (texts.Count <= opts.BatchSize)
            return await SendBatchRequestAsync(texts, opts, ct);

        // Chunk into multiple batches
        var allResults = new List<EmbeddingResult>(texts.Count);
        for (int offset = 0; offset < texts.Count; offset += opts.BatchSize)
        {
            var batch = texts.Skip(offset).Take(opts.BatchSize).ToList();
            var batchResults = await SendBatchRequestAsync(batch, opts, ct);
            allResults.AddRange(batchResults);
        }

        return allResults;
    }

    private async Task<IReadOnlyList<EmbeddingResult>> SendBatchRequestAsync(
        IReadOnlyList<string> texts,
        EmbeddingsOptions opts,
        CancellationToken ct)
    {
        var apiKey = llmOptions.Value.Providers.TryGetValue("openrouter", out var providerCfg)
            ? providerCfg.ApiKey
            : string.Empty;

        object input = texts.Count == 1 ? (object)texts[0] : texts.ToArray();

        var requestBody = new EmbeddingRequest(
            Model: opts.Model,
            Input: input,
            EncodingFormat: "float",
            Provider: new EmbeddingProviderOptions(AllowFallbacks: opts.AllowFallbacks));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(opts.RequestTimeoutSeconds));

        // Not disposed: IHttpClientFactory owns the lifetime of clients it hands out.
        var httpClient = httpClientFactory.CreateClient("embeddings");
        var request = new HttpRequestMessage(HttpMethod.Post, "embeddings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(requestBody, options: JsonOpts);

        logger.LogDebug("OpenRouter embedding request: model={Model} count={Count}", opts.Model, texts.Count);

        using var response = await httpClient.SendAsync(request, cts.Token);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cts.Token);
            throw new HttpRequestException($"OpenRouter embeddings returned {(int)response.StatusCode}: {body}");
        }

        var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(JsonOpts, cts.Token)
            ?? throw new InvalidOperationException("OpenRouter embeddings returned an empty response body.");

        var costPerItem = CalculateCostPerItem(result.Usage.TotalTokens, texts.Count, opts);

        return result.Data
            .OrderBy(d => d.Index)
            .Select(d => new EmbeddingResult(
                Vector: d.Embedding,
                TokenCount: result.Usage.TotalTokens / texts.Count,
                CostEur: costPerItem))
            .ToList();
    }

    private static decimal? CalculateCostPerItem(int totalTokens, int itemCount, EmbeddingsOptions opts)
    {
        if (totalTokens <= 0 || opts.CostPerMillionTokensUsd <= 0)
            return null;

        var totalCost = (decimal)(totalTokens / 1_000_000.0 * opts.CostPerMillionTokensUsd * opts.UsdToEurRate);
        return totalCost / itemCount;
    }

    // ---- JSON DTOs (snake_case via JsonPropertyName) ----

    private sealed record EmbeddingRequest(
        [property: JsonPropertyName("model")]           string Model,
        [property: JsonPropertyName("input")]           object Input,
        [property: JsonPropertyName("encoding_format")] string EncodingFormat,
        [property: JsonPropertyName("provider")]        EmbeddingProviderOptions Provider);

    private sealed record EmbeddingProviderOptions(
        [property: JsonPropertyName("allow_fallbacks")] bool AllowFallbacks);

    private sealed record EmbeddingResponse(
        [property: JsonPropertyName("data")]  IReadOnlyList<EmbeddingData> Data,
        [property: JsonPropertyName("usage")] EmbeddingUsage Usage);

    private sealed record EmbeddingData(
        [property: JsonPropertyName("index")]     int Index,
        [property: JsonPropertyName("embedding")] float[] Embedding);

    private sealed record EmbeddingUsage(
        [property: JsonPropertyName("total_tokens")] int TotalTokens);
}
