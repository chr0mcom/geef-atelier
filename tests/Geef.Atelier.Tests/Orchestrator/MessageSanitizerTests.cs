using System.Net;
using Geef.Atelier.Web.Services;

namespace Geef.Atelier.Tests.Orchestrator;

/// <summary>
/// Unit tests for <see cref="RunOrchestratorService.SanitizeErrorMessage"/>.
/// User-visible messages are fixed plain-English sentences: informative, but never text taken from the exception
/// (keys, hosts, variable names, paths).
/// </summary>
public sealed class MessageSanitizerTests
{
    [Theory]
    [InlineData(HttpStatusCode.BadRequest,          "400")]
    [InlineData(HttpStatusCode.Unauthorized,        "authentication")]
    [InlineData(HttpStatusCode.Forbidden,           "denied access")]
    [InlineData(HttpStatusCode.TooManyRequests,     "rate limit")]
    [InlineData(HttpStatusCode.InternalServerError, "unavailable")]
    [InlineData(HttpStatusCode.ServiceUnavailable,  "unavailable")]
    [InlineData(HttpStatusCode.BadGateway,          "unavailable")]
    public void SanitizesHttpStatusCodes(HttpStatusCode code, string expectedFragment)
    {
        var ex  = new HttpRequestException($"Bearer sk-secret-key-12345 rejected", null, code);
        var msg = RunOrchestratorService.SanitizeErrorMessage(ex);

        Assert.Contains(expectedFragment, msg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-secret", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SanitizesHttpRequestException_WithoutStatusCode()
    {
        var ex  = new HttpRequestException("Connection refused: https://api.example.com/v1/chat?key=sk-abc123");
        var msg = RunOrchestratorService.SanitizeErrorMessage(ex);

        Assert.Contains("request failed", msg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-abc123", msg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api.example.com", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SanitizesTaskCanceledException()
    {
        var ex  = new TaskCanceledException("The request timed out after 30 seconds.");
        var msg = RunOrchestratorService.SanitizeErrorMessage(ex);

        Assert.Contains("timed out", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SanitizesGenericException_UsesAFixedSentenceWithoutTheMessage()
    {
        var ex  = new InvalidOperationException("First line of message.\nSecond line with sk-secret details.");
        var msg = RunOrchestratorService.SanitizeErrorMessage(ex);

        Assert.Equal(RunOrchestratorService.UnexpectedErrorMessage, msg);
        Assert.DoesNotContain("First line", msg);
        Assert.DoesNotContain("sk-secret", msg);
    }

    [Fact]
    public void SanitizesHttpRequestException_429()
    {
        var ex  = new HttpRequestException("Rate limit exceeded", null, HttpStatusCode.TooManyRequests);
        var msg = RunOrchestratorService.SanitizeErrorMessage(ex);

        Assert.Contains("rate limit", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Provider rejected key sk-or-v1-0123456789abcdef0123456789abcdef0123", "sk-or-v1")]
    [InlineData("Unauthorized: Authorization: Bearer abcdefghijklmnop.qrstuvwxyz", "abcdefghijklmnop")]
    [InlineData("call failed for https://api.example.org/search?api_key=tvly-ABCDEF0123456789XYZ", "api.example.org")]
    [InlineData("config error: password=hunter2hunter2", "hunter2")]
    [InlineData("TAVILY_API_KEY environment variable is not set", "TAVILY_API_KEY")]
    [InlineData("Connection refused (database-host:5432)", "database-host")]
    [InlineData("Could not find file '/var/lib/app/data/x.json'", "/var/lib/app")]
    public void SanitizesGenericException_NeverShowsKeysHostsVariablesOrPaths(string message, string fragment)
    {
        var msg = RunOrchestratorService.SanitizeErrorMessage(new InvalidOperationException(message));

        Assert.Equal(RunOrchestratorService.UnexpectedErrorMessage, msg);
        Assert.DoesNotContain(fragment, msg);
    }

    [Fact]
    public void SanitizesWrappedHttpErrors_ByTheirStatusCode()
    {
        var ex = new InvalidOperationException("wrapped", new HttpRequestException("upstream sk-abc", null, HttpStatusCode.TooManyRequests));

        var msg = RunOrchestratorService.SanitizeErrorMessage(ex);

        Assert.Contains("rate limit", msg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-abc", msg);
    }
}
