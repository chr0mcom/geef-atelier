using System.Net;
using Geef.Atelier.Infrastructure.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Llm;

/// <summary>
/// Transport-level tests for <see cref="CliProxyModelSource"/>: URL shape, provenance parsing and
/// the guarantee that no backend problem ever escapes as an exception.
/// </summary>
public sealed class CliProxyModelSourceTests
{
    private static CliProxyModelSource Build(RecordingHandler handler, string baseUrl = "http://cli-proxy:8090")
        => new(
            new SingleClientFactory(new HttpClient(handler)),
            Options.Create(new CliProxyOptions { BaseUrl = baseUrl }),
            NullLogger<CliProxyModelSource>.Instance);

    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task BuildsTheProviderKeyedUrl()
    {
        var handler = RecordingHandler.Ok("""{"object":"list","data":[{"id":"claude-opus-5"}],"x_source":"live"}""");

        await Build(handler).TryGetCatalogAsync("claude-cli", Budget, bypassProxyCache: false);

        Assert.Equal("http://cli-proxy:8090/v1/cli/claude-cli/models", handler.LastUrl);
    }

    [Fact]
    public async Task ToleratesATrailingSlashOnTheBaseUrl()
    {
        var handler = RecordingHandler.Ok("""{"object":"list","data":[{"id":"claude-opus-5"}],"x_source":"live"}""");

        await Build(handler, "http://cli-proxy:8090/").TryGetCatalogAsync("claude-cli", Budget, bypassProxyCache: false);

        Assert.Equal("http://cli-proxy:8090/v1/cli/claude-cli/models", handler.LastUrl);
    }

    [Fact]
    public async Task AppendsTheRefreshFlag_WhenBypassingTheBackendCache()
    {
        var handler = RecordingHandler.Ok("""{"object":"list","data":[{"id":"claude-opus-5"}],"x_source":"live"}""");

        await Build(handler).TryGetCatalogAsync("claude-cli", Budget, bypassProxyCache: true);

        Assert.EndsWith("/v1/cli/claude-cli/models?refresh=1", handler.LastUrl);
    }

    [Fact]
    public async Task ParsesLiveSourceAndAliasMap()
    {
        var handler = RecordingHandler.Ok("""
            {"object":"list","data":[{"id":"claude-opus-latest"},{"id":"claude-opus-5"}],
             "x_source":"live","x_aliases":{"claude-opus-latest":"claude-opus-5"}}
            """);

        var result = await Build(handler).TryGetCatalogAsync("claude-cli", Budget, bypassProxyCache: false);

        Assert.NotNull(result);
        Assert.True(result!.IsLive);
        Assert.True(result.HasLiveSource);
        Assert.Equal(["claude-opus-latest", "claude-opus-5"], result.ModelIds);
        Assert.Equal("claude-opus-5", result.AliasMap["claude-opus-latest"]);
    }

    [Fact]
    public async Task DegradedSourceIsReportedAsNotLive_ButStillHasALiveSource()
    {
        var handler = RecordingHandler.Ok("""{"object":"list","data":[{"id":"claude-opus-4-8"}],"x_source":"degraded"}""");

        var result = await Build(handler).TryGetCatalogAsync("claude-cli", Budget, bypassProxyCache: false);

        Assert.NotNull(result);
        Assert.False(result!.IsLive);
        Assert.True(result.HasLiveSource);
    }

    [Fact]
    public async Task StaticSourceIsReportedAsHavingNoLiveSource()
    {
        var handler = RecordingHandler.Ok("""{"object":"list","data":[{"id":"gemini-3-1-pro"}],"x_source":"static"}""");

        var result = await Build(handler).TryGetCatalogAsync("gemini-cli", Budget, bypassProxyCache: false);

        Assert.NotNull(result);
        Assert.False(result!.IsLive);
        Assert.False(result.HasLiveSource);
    }

    [Fact]
    public async Task ReturnsNull_OnErrorStatus()
    {
        var handler = RecordingHandler.Fail(HttpStatusCode.BadGateway);

        Assert.Null(await Build(handler).TryGetCatalogAsync("claude-cli", Budget, bypassProxyCache: false));
    }

    [Fact]
    public async Task ReturnsNull_OnEmptyData()
    {
        var handler = RecordingHandler.Ok("""{"object":"list","data":[],"x_source":"live"}""");

        Assert.Null(await Build(handler).TryGetCatalogAsync("claude-cli", Budget, bypassProxyCache: false));
    }

    [Fact]
    public async Task ReturnsNull_OnForeignJsonWithoutIds()
    {
        var handler = RecordingHandler.Ok("""{"hello":"world"}""");

        Assert.Null(await Build(handler).TryGetCatalogAsync("claude-cli", Budget, bypassProxyCache: false));
    }

    [Fact]
    public async Task ReturnsNull_OnUnparsableBody()
    {
        var handler = RecordingHandler.Ok("not json at all");

        Assert.Null(await Build(handler).TryGetCatalogAsync("claude-cli", Budget, bypassProxyCache: false));
    }

    [Fact]
    public async Task ReturnsNull_OnTimeout()
    {
        var handler = RecordingHandler.Hanging();

        Assert.Null(await Build(handler).TryGetCatalogAsync("claude-cli", TimeSpan.FromMilliseconds(30), bypassProxyCache: false));
    }

    [Fact]
    public async Task ReturnsNull_WhenBaseUrlIsMissing()
    {
        var handler = RecordingHandler.Ok("""{"object":"list","data":[{"id":"x"}]}""");

        Assert.Null(await Build(handler, "  ").TryGetCatalogAsync("claude-cli", Budget, bypassProxyCache: false));
        Assert.Null(handler.LastUrl);
    }

    [Fact]
    public async Task PropagatesCallerCancellation()
    {
        var handler = RecordingHandler.Hanging();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Build(handler).TryGetCatalogAsync("claude-cli", Budget, bypassProxyCache: false, cts.Token));
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(HttpStatusCode status, string? body, bool hang = false) : HttpMessageHandler
    {
        public string? LastUrl { get; private set; }

        public static RecordingHandler Ok(string body) => new(HttpStatusCode.OK, body);
        public static RecordingHandler Fail(HttpStatusCode code) => new(code, null);
        public static RecordingHandler Hanging() => new(HttpStatusCode.OK, null, hang: true);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUrl = request.RequestUri?.ToString();

            if (hang)
                await Task.Delay(Timeout.Infinite, cancellationToken);

            var resp = new HttpResponseMessage(status);
            if (body is not null)
                resp.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            return resp;
        }
    }
}

/// <summary>
/// The named client's cap is the ceiling every per-attempt budget lives under. When it sits below a
/// budget, that budget is silently ineffective — the defect that made the restored 240 s warm-up
/// budget meaningless while looking correct in configuration.
/// </summary>
public sealed class ModelProbeClientTimeoutTests
{
    [Theory]
    [InlineData(5, 30, 240)]
    [InlineData(5, 30, 60)]
    [InlineData(1, 1, 1)]
    public void TheClientCapStaysAboveEveryBudget(int interactive, int refresh, int warmUp)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmClient(BuildConfiguration(interactive, refresh, warmUp));

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientNamesForTest.CliProxyModels);

        var largestBudget = TimeSpan.FromSeconds(Math.Max(warmUp, Math.Max(refresh, interactive)));
        Assert.True(client.Timeout > largestBudget,
            $"client cap {client.Timeout} must exceed the largest budget {largestBudget}");
    }

    private static IConfiguration BuildConfiguration(int interactive, int refresh, int warmUp) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ModelCatalog:InteractiveTimeoutSeconds"] = interactive.ToString(),
            ["ModelCatalog:RefreshTimeoutSeconds"] = refresh.ToString(),
            ["ModelCatalog:WarmUpTimeoutSeconds"] = warmUp.ToString(),
        }).Build();

    private static class HttpClientNamesForTest
    {
        internal const string CliProxyModels = "cli-proxy-models";
    }
}
