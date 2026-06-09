using System.Net;
using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Tests.Infrastructure.Llm;

/// <summary>
/// Unit tests for <see cref="HttpTransientFaultClassifier"/>: verifies that HTTP and timeout
/// exceptions are correctly classified as transient or permanent.
/// </summary>
public sealed class HttpTransientFaultClassifierTests
{
    private readonly HttpTransientFaultClassifier _classifier = new();

    // ── Transient: server / rate-limit / timeout ─────────────────────────────

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)] // 500
    [InlineData(HttpStatusCode.BadGateway)]          // 502
    [InlineData(HttpStatusCode.ServiceUnavailable)]  // 503
    [InlineData(HttpStatusCode.GatewayTimeout)]      // 504
    [InlineData((HttpStatusCode)429)]                // TooManyRequests
    [InlineData((HttpStatusCode)408)]                // RequestTimeout
    public void IsTransient_ReturnsTrueFor_RetryableHttpErrors(HttpStatusCode code)
    {
        var ex = new HttpRequestException("error", null, code);
        Assert.True(_classifier.IsTransient(ex));
    }

    [Fact]
    public void IsTransient_ReturnsTrueFor_HttpRequestExceptionWithoutStatusCode()
    {
        var ex = new HttpRequestException("connection refused");
        Assert.True(_classifier.IsTransient(ex));
    }

    [Fact]
    public void IsTransient_ReturnsTrueFor_TaskCanceledException()
    {
        Assert.True(_classifier.IsTransient(new TaskCanceledException("timeout")));
    }

    [Fact]
    public void IsTransient_ReturnsTrueFor_TimeoutException()
    {
        Assert.True(_classifier.IsTransient(new TimeoutException()));
    }

    // ── Permanent: auth / bad request / not found ────────────────────────────

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]   // 401
    [InlineData(HttpStatusCode.Forbidden)]      // 403
    [InlineData(HttpStatusCode.NotFound)]       // 404
    [InlineData(HttpStatusCode.BadRequest)]     // 400
    [InlineData(HttpStatusCode.UnprocessableEntity)] // 422
    public void IsTransient_ReturnsFalseFor_PermanentHttpErrors(HttpStatusCode code)
    {
        var ex = new HttpRequestException("error", null, code);
        Assert.False(_classifier.IsTransient(ex));
    }

    [Fact]
    public void IsTransient_ReturnsFalseFor_GenericException()
    {
        Assert.False(_classifier.IsTransient(new InvalidOperationException("oops")));
    }

    // ── Inner exception inspection ────────────────────────────────────────────

    [Fact]
    public void IsTransient_InspectsInnerExceptions_ForTransientCause()
    {
        var inner = new HttpRequestException("gateway timeout", null, HttpStatusCode.GatewayTimeout);
        var outer = new InvalidOperationException("wrapped", inner);
        Assert.True(_classifier.IsTransient(outer));
    }

    [Fact]
    public void IsTransient_InspectsInnerExceptions_ForPermanentCause()
    {
        var inner = new HttpRequestException("unauthorized", null, HttpStatusCode.Unauthorized);
        var outer = new InvalidOperationException("wrapped", inner);
        Assert.False(_classifier.IsTransient(outer));
    }
}
