using Geef.Atelier.Application.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Infrastructure.Finalizers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;

namespace Geef.Atelier.Tests.Infrastructure.Finalizers;

public sealed class ExternalSinkFinalizerExecutorTests
{
    private static FinalizerExecutionContext MakeContext() => new(
        RunId: Guid.NewGuid(),
        TemplateName: "test-template",
        FinalText: "Final text content.",
        CurrentText: "Final text content.",
        RunCompletedAt: DateTimeOffset.UtcNow);

    private static FinalizerProfile WebhookProfile(string url, string? authHeader = null) => new(
        Name: "webhook-sink",
        DisplayName: "Webhook Sink",
        Description: "test",
        FinalizerType: FinalizerType.ExternalSink,
        Settings: new WebhookSinkSettings(url, authHeader, "application/json", 5).ToDict(),
        IsSystem: true);

    [Fact]
    public async Task Execute_Webhook_OnSuccess_ProducesUrlArtifact()
    {
        var handler = new SuccessHandler();
        var factory = new FakeHttpClientFactory(handler);
        var executor = BuildExecutor(factory);

        var result = await executor.ExecuteAsync(
            WebhookProfile("https://hook.example.com/endpoint"), MakeContext(), default);

        Assert.NotNull(result.Artifact);
        Assert.Equal(ArtifactType.Url, result.Artifact!.ArtifactType);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Execute_Webhook_OnHttpError_ProducesStatusArtifact()
    {
        var handler = new FailingHandler(HttpStatusCode.InternalServerError);
        var factory = new FakeHttpClientFactory(handler);
        var executor = BuildExecutor(factory);

        var result = await executor.ExecuteAsync(
            WebhookProfile("https://hook.example.com/endpoint"), MakeContext(), default);

        Assert.NotNull(result.Artifact);
        Assert.Equal(ArtifactType.Status, result.Artifact.ArtifactType);
    }

    [Fact]
    public async Task Execute_Webhook_SendsAuthorizationHeader_WhenConfigured()
    {
        var handler = new CapturingHandler();
        var factory = new FakeHttpClientFactory(handler);
        var executor = BuildExecutor(factory);

        await executor.ExecuteAsync(
            WebhookProfile("https://hook.example.com/endpoint", "Bearer secret123"),
            MakeContext(), default);

        Assert.True(handler.LastRequest?.Headers.Contains("Authorization"),
            "Authorization header should be present");
    }

    [Fact]
    public async Task Execute_Webhook_DoesNotIncludeAuthHeader_WhenNotConfigured()
    {
        var handler = new CapturingHandler();
        var factory = new FakeHttpClientFactory(handler);
        var executor = BuildExecutor(factory);

        await executor.ExecuteAsync(
            WebhookProfile("https://hook.example.com/endpoint", authHeader: null),
            MakeContext(), default);

        Assert.False(handler.LastRequest?.Headers.Contains("Authorization"),
            "Authorization header should not be present when not configured");
    }

    [Fact]
    public async Task Execute_DoesNotUpdateText()
    {
        var handler = new SuccessHandler();
        var factory = new FakeHttpClientFactory(handler);
        var executor = BuildExecutor(factory);

        var result = await executor.ExecuteAsync(
            WebhookProfile("https://hook.example.com/endpoint"), MakeContext(), default);

        Assert.Null(result.UpdatedText);
        Assert.Null(result.CostEur);
    }

    private static ExternalSinkFinalizerExecutor BuildExecutor(IHttpClientFactory factory) =>
        new(factory,
            Options.Create(new FinalizerOptions()),
            NullLogger<ExternalSinkFinalizerExecutor>.Instance);

    // ---------------------------------------------------------------------------
    // Fakes
    // ---------------------------------------------------------------------------

    private sealed class SuccessHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class FailingHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }
}
