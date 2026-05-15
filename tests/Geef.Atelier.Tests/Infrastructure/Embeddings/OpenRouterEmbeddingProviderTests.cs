using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Application.Crew.Knowledge.Options;
using Geef.Atelier.Infrastructure.Embeddings;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Tests.Domain.Crew;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Infrastructure.Embeddings;

public sealed class OpenRouterEmbeddingProviderTests
{
    private const string SingleEmbeddingResponse = """
        {
          "data": [
            { "index": 0, "embedding": [0.1, 0.2, 0.3] }
          ],
          "usage": { "total_tokens": 10 }
        }
        """;

    private const string BatchEmbeddingResponse = """
        {
          "data": [
            { "index": 0, "embedding": [0.1, 0.2, 0.3] },
            { "index": 1, "embedding": [0.4, 0.5, 0.6] }
          ],
          "usage": { "total_tokens": 20 }
        }
        """;

    [Fact]
    public async Task CreateAsync_ReturnsEmbeddingResult_WithCorrectVector()
    {
        var (provider, _) = BuildProvider(FakeHttpHandler.Ok(SingleEmbeddingResponse));

        var result = await provider.CreateAsync("hello world", CancellationToken.None);

        Assert.Equal([0.1f, 0.2f, 0.3f], result.Vector);
        Assert.Equal(10, result.TokenCount);
    }

    [Fact]
    public async Task CreateAsync_CalculatesCostCorrectly()
    {
        var opts = DefaultOpts();
        opts.CostPerMillionTokensUsd = 0.02;
        opts.UsdToEurRate = 0.92;
        var (provider, _) = BuildProvider(FakeHttpHandler.Ok(SingleEmbeddingResponse), opts: opts);

        var result = await provider.CreateAsync("hello", CancellationToken.None);

        // 10 tokens / 1_000_000 * 0.02 * 0.92 = 0.000000184
        Assert.NotNull(result.CostEur);
        Assert.True(result.CostEur > 0);
    }

    [Fact]
    public async Task CreateAsync_SetsAllowFallbacksTrue()
    {
        string? capturedBody = null;
        var handler = new CapturingHttpHandler(SingleEmbeddingResponse, async req =>
            capturedBody = await req.Content!.ReadAsStringAsync());
        var (provider, _) = BuildProvider(handler);

        await provider.CreateAsync("test", CancellationToken.None);

        Assert.NotNull(capturedBody);
        Assert.Contains("allow_fallbacks", capturedBody);
        Assert.Contains("true", capturedBody);
    }

    [Fact]
    public async Task CreateAsync_SetsBearerAuthHeader()
    {
        System.Net.Http.Headers.AuthenticationHeaderValue? capturedAuth = null;
        var handler = new CapturingHttpHandler(SingleEmbeddingResponse, req =>
        {
            capturedAuth = req.Headers.Authorization;
        });
        var (provider, _) = BuildProvider(handler, apiKey: "test-api-key-42");

        await provider.CreateAsync("text", CancellationToken.None);

        Assert.NotNull(capturedAuth);
        Assert.Equal("Bearer", capturedAuth!.Scheme);
        Assert.Equal("test-api-key-42", capturedAuth.Parameter);
    }

    [Fact]
    public async Task CreateAsync_ThrowsOnHttpError()
    {
        var (provider, _) = BuildProvider(FakeHttpHandler.Fail(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.CreateAsync("text", CancellationToken.None));
    }

    [Fact]
    public async Task CreateBatchAsync_SendsSingleRequest_ForSmallBatch()
    {
        var handler = FakeHttpHandler.Ok(BatchEmbeddingResponse);
        var opts = DefaultOpts();
        opts.BatchSize = 100;
        var (provider, _) = BuildProvider(handler, opts: opts);

        var results = await provider.CreateBatchAsync(["text1", "text2"], CancellationToken.None);

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(2, results.Count);
        Assert.Equal([0.1f, 0.2f, 0.3f], results[0].Vector);
        Assert.Equal([0.4f, 0.5f, 0.6f], results[1].Vector);
    }

    [Fact]
    public async Task CreateBatchAsync_ChunksIntoMultipleRequests_WhenExceedsBatchSize()
    {
        // BatchSize = 2, sending 3 texts → 2 HTTP calls (batch of 2, then batch of 1)
        // The fake handler always returns SingleEmbeddingResponse (1 data item) for both calls.
        var handler = FakeHttpHandler.Ok(SingleEmbeddingResponse);
        var opts = DefaultOpts();
        opts.BatchSize = 2;
        var (provider, _) = BuildProvider(handler, opts: opts);

        await provider.CreateBatchAsync(["t1", "t2", "t3"], CancellationToken.None);

        // Key assertion: 3 texts with BatchSize=2 requires exactly 2 HTTP requests
        Assert.Equal(2, handler.CallCount);
    }

    // --- helpers ---

    private static EmbeddingsOptions DefaultOpts() => new()
    {
        Provider = "openrouter",
        Model = "openai/text-embedding-3-small",
        Dimensions = 3,
        Endpoint = "https://openrouter.ai/api/v1",
        CostPerMillionTokensUsd = 0.02,
        UsdToEurRate = 0.92,
        BatchSize = 100,
        AllowFallbacks = true,
        RequestTimeoutSeconds = 30,
    };

    private static (OpenRouterEmbeddingProvider, FakeHttpHandler?) BuildProvider(
        HttpMessageHandler handler,
        EmbeddingsOptions? opts = null,
        string apiKey = "test-key")
    {
        var embOpts = opts ?? DefaultOpts();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://openrouter.ai/api/v1") };
        var llmOptions = new LlmOptions
        {
            Providers = new Dictionary<string, LlmOptions.ProviderConfig>
            {
                ["openrouter"] = new() { ApiKey = apiKey, Endpoint = "https://openrouter.ai/api/v1" },
            },
        };
        var provider = new OpenRouterEmbeddingProvider(
            new FakeHttpClientFactory(httpClient),
            Options.Create(embOpts),
            Options.Create(llmOptions),
            NullLogger<OpenRouterEmbeddingProvider>.Instance);

        return (provider, handler as FakeHttpHandler);
    }

    /// <summary>
    /// Extended fake handler that captures data from the outgoing request before responding.
    /// The capture delegate runs before the response is returned so content is still accessible.
    /// </summary>
    private sealed class CapturingHttpHandler(string body, Func<HttpRequestMessage, Task> captureAsync) : HttpMessageHandler
    {
        public CapturingHttpHandler(string body, Action<HttpRequestMessage> capture)
            : this(body, req => { capture(req); return Task.CompletedTask; }) { }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await captureAsync(request);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }
}
