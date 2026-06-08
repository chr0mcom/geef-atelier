using System.Net;
using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Tests.Infrastructure.Llm;

/// <summary>
/// Unit tests for <see cref="LlmResilience"/>: transient classification and the retry loop. All
/// retry tests use a zero base delay so they run instantly.
/// </summary>
public sealed class LlmResilienceTests
{
    private static readonly TimeSpan NoDelay = TimeSpan.Zero;

    // ── IsTransient ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)] // 500
    [InlineData(HttpStatusCode.BadGateway)]          // 502
    [InlineData(HttpStatusCode.ServiceUnavailable)]  // 503
    [InlineData(HttpStatusCode.GatewayTimeout)]      // 504
    [InlineData(HttpStatusCode.TooManyRequests)]     // 429
    [InlineData(HttpStatusCode.RequestTimeout)]      // 408
    public void IsTransient_TrueFor_RetryableHttpStatus(HttpStatusCode code)
    {
        var ex = new HttpRequestException("boom", null, code);
        Assert.True(LlmResilience.IsTransient(ex));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]   // 400
    [InlineData(HttpStatusCode.Unauthorized)] // 401
    [InlineData(HttpStatusCode.Forbidden)]    // 403
    [InlineData(HttpStatusCode.NotFound)]     // 404
    public void IsTransient_FalseFor_PermanentClientErrors(HttpStatusCode code)
    {
        var ex = new HttpRequestException("nope", null, code);
        Assert.False(LlmResilience.IsTransient(ex));
    }

    [Fact]
    public void IsTransient_TrueFor_HttpRequestExceptionWithoutStatus()
        => Assert.True(LlmResilience.IsTransient(new HttpRequestException("connection reset")));

    [Fact]
    public void IsTransient_TrueFor_TimeoutLikeExceptions()
    {
        Assert.True(LlmResilience.IsTransient(new TaskCanceledException("timed out")));
        Assert.True(LlmResilience.IsTransient(new TimeoutException("slow")));
    }

    [Fact]
    public void IsTransient_FalseFor_GenericException()
        => Assert.False(LlmResilience.IsTransient(new InvalidOperationException("logic bug")));

    [Fact]
    public void IsTransient_WalksInnerExceptionChain()
    {
        var wrapped = new InvalidOperationException("outer",
            new HttpRequestException("inner", null, HttpStatusCode.ServiceUnavailable));
        Assert.True(LlmResilience.IsTransient(wrapped));
    }

    // ── ExecuteAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ReturnsResult_OnFirstSuccess()
    {
        var calls = 0;
        var result = await LlmResilience.ExecuteAsync(
            _ => { calls++; return Task.FromResult(42); },
            CancellationToken.None, baseDelay: NoDelay);

        Assert.Equal(42, result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ExecuteAsync_RetriesTransient_ThenSucceeds()
    {
        var calls = 0;
        var result = await LlmResilience.ExecuteAsync(
            _ =>
            {
                calls++;
                if (calls < 3)
                    throw new HttpRequestException("temporary", null, HttpStatusCode.ServiceUnavailable);
                return Task.FromResult("ok");
            },
            CancellationToken.None, maxAttempts: 4, baseDelay: NoDelay);

        Assert.Equal("ok", result);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotRetry_PermanentError()
    {
        var calls = 0;
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            LlmResilience.ExecuteAsync<int>(
                _ =>
                {
                    calls++;
                    throw new HttpRequestException("auth", null, HttpStatusCode.Unauthorized);
                },
                CancellationToken.None, maxAttempts: 4, baseDelay: NoDelay));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ExecuteAsync_ExhaustsAttempts_ThenThrowsLastException()
    {
        var calls = 0;
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            LlmResilience.ExecuteAsync<int>(
                _ =>
                {
                    calls++;
                    throw new HttpRequestException("still down", null, HttpStatusCode.InternalServerError);
                },
                CancellationToken.None, maxAttempts: 3, baseDelay: NoDelay));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotRetry_WhenCallerTokenIsCancelled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var calls = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            LlmResilience.ExecuteAsync<int>(
                _ =>
                {
                    calls++;
                    throw new TaskCanceledException("cancelled");
                },
                cts.Token, maxAttempts: 4, baseDelay: NoDelay));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ExecuteAsync_InvokesOnRetryCallback_PerRetry()
    {
        var retries = 0;
        await LlmResilience.ExecuteAsync(
            _ =>
            {
                if (retries < 2)
                    throw new HttpRequestException("blip", null, HttpStatusCode.BadGateway);
                return Task.FromResult(1);
            },
            CancellationToken.None, maxAttempts: 5, baseDelay: NoDelay,
            onRetry: (_, _, _) => retries++);

        Assert.Equal(2, retries);
    }
}
